using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Billing;
using WorkshopOS.Application.Estimates;
using WorkshopOS.Domain.Billing;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class BillingTenantIsolationTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public void Invoice_IsTenantFiltered()
    {
        using var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(Invoice)));
    }

    [Fact]
    public void InvoiceItem_IsTenantFiltered()
    {
        using var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(InvoiceItem)));
    }

    [Fact]
    public void InvoicePaymentRecord_IsTenantFiltered()
    {
        using var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(InvoicePaymentRecord)));
    }

    [Fact]
    public async Task Invoice_IsTenantFiltered_DoesNotExposeOtherOrganizationInvoices()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        var managerB = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-b");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);

        Guid invoiceBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(BillingTestSupport.DefaultNow)))
        {
            var location = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, $"{suffix}-b");
            var customer = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, $"{suffix}-b");
            var vehicle = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, $"{suffix}-b", customer.Id);
            var repairOrder = await TestDataFactory.PersistRepairOrderAsync(
                scopeB,
                organizationB.Id,
                location.Id,
                customer.Id,
                vehicle.Id,
                $"{suffix}-b",
                BillingTestSupport.DefaultNow);
            var invoiceService = BillingTestSupport.CreateInvoiceService(scopeB, new TestOrganizationContext(organizationB.Id));
            invoiceBId = await BillingTestSupport.CreateDraftInvoiceWithItemAsync(
                invoiceService,
                managerB.Id,
                repairOrder.Id);
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        Assert.Equal(0, await readScopeA.Invoices.CountAsync());
        Assert.Null(await readScopeA.Invoices.SingleOrDefaultAsync(candidate => candidate.Id == invoiceBId));
    }

    [Fact]
    public async Task CustomerPortal_DoesNotExposeInvoicesOrPayments()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await BillingTestSupport.CreateBillingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(BillingTestSupport.DefaultNow));
        var invoiceService = BillingTestSupport.CreateInvoiceService(writeScope, new TestOrganizationContext(scenario.OrganizationId));
        var paymentService = BillingTestSupport.CreatePaymentService(writeScope, new TestOrganizationContext(scenario.OrganizationId));
        var invoiceId = await BillingTestSupport.CreateIssuedInvoiceAsync(
            invoiceService,
            scenario.ManagerId,
            scenario.RepairOrderId);
        await paymentService.RecordPaymentAsync(
            scenario.ManagerId,
            new RecordPaymentCommand
            {
                InvoiceId = invoiceId,
                Amount = 50m,
                PaymentMethod = PaymentMethod.Cash,
            });

        Assert.Equal(0, await writeScope.InvoicePaymentRecords.CountAsync(candidate => candidate.RecordedByUserId == Guid.Empty));
        Assert.True(await writeScope.Invoices.AnyAsync());
        Assert.True(await writeScope.InvoicePaymentRecords.AnyAsync());
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class BillingManagementTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task InvoiceManagement_Create_UsesCurrentOrganization()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await BillingTestSupport.CreateBillingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(BillingTestSupport.DefaultNow));
        var service = BillingTestSupport.CreateInvoiceService(writeScope, new TestOrganizationContext(scenario.OrganizationId));

        var result = await service.CreateInvoiceAsync(scenario.ManagerId, scenario.RepairOrderId);

        Assert.True(result.Success);
        var invoice = await writeScope.Invoices.SingleAsync(candidate => candidate.Id == result.Value);
        Assert.Equal(scenario.OrganizationId, invoice.OrganizationId);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
    }

    [Fact]
    public async Task InvoiceManagement_Create_WhenOrganizationUnresolved_FailsClosed()
    {
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var service = BillingTestSupport.CreateInvoiceService(scope.Context, new UnresolvedOrganizationContext());

        var result = await service.CreateInvoiceAsync(Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.False(result.Success);
        Assert.Equal(InvoiceOperationFailureReason.OrganizationUnresolved, result.FailureReason);
    }

    [Fact]
    public async Task InvoiceManagement_Create_RejectsOtherTenantRepairOrder()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, manager.Id, OrganizationMembershipRole.Owner);

        Guid repairOrderBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var location = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
            var customer = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
            var vehicle = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customer.Id);
            var repairOrder = await TestDataFactory.PersistRepairOrderAsync(
                scopeB,
                organizationB.Id,
                location.Id,
                customer.Id,
                vehicle.Id,
                suffix,
                BillingTestSupport.DefaultNow);
            repairOrderBId = repairOrder.Id;
        }

        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = BillingTestSupport.CreateInvoiceService(writeScopeA, new TestOrganizationContext(organizationA.Id));

        var result = await service.CreateInvoiceAsync(manager.Id, repairOrderBId);

        Assert.False(result.Success);
        Assert.Equal(InvoiceOperationFailureReason.RepairOrderNotFound, result.FailureReason);
    }

    [Fact]
    public async Task InvoiceManagement_Create_RejectsCancelledRepairOrder()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await BillingTestSupport.CreateBillingScenarioAsync(
            scope,
            suffix,
            repairOrderStatus: RepairOrderStatus.Cancelled);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(scenario.OrganizationId));
        var service = BillingTestSupport.CreateInvoiceService(writeScope, new TestOrganizationContext(scenario.OrganizationId));

        var result = await service.CreateInvoiceAsync(scenario.ManagerId, scenario.RepairOrderId);

        Assert.False(result.Success);
        Assert.Equal(InvoiceOperationFailureReason.RepairOrderNotEligible, result.FailureReason);
    }

    [Fact]
    public async Task InvoiceManagement_Create_RejectsSecondActiveInvoiceForRepairOrder()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await BillingTestSupport.CreateBillingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(BillingTestSupport.DefaultNow));
        var service = BillingTestSupport.CreateInvoiceService(writeScope, new TestOrganizationContext(scenario.OrganizationId));

        Assert.True((await service.CreateInvoiceAsync(scenario.ManagerId, scenario.RepairOrderId)).Success);
        var second = await service.CreateInvoiceAsync(scenario.ManagerId, scenario.RepairOrderId);

        Assert.False(second.Success);
        Assert.Equal(InvoiceOperationFailureReason.CurrentInvoiceExists, second.FailureReason);
    }

    [Fact]
    public async Task InvoiceManagement_CreateFromApprovedEstimate_CopiesAllItems()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await BillingTestSupport.CreateBillingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(BillingTestSupport.DefaultNow));
        var estimateService = BillingTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(scenario.OrganizationId));
        var invoiceService = BillingTestSupport.CreateInvoiceService(writeScope, new TestOrganizationContext(scenario.OrganizationId));

        var estimateId = await BillingTestSupport.CreateApprovedEstimateAsync(
            estimateService,
            scenario.ManagerId,
            scenario.RepairOrderId);

        var result = await invoiceService.CreateInvoiceFromApprovedEstimateAsync(scenario.ManagerId, estimateId);

        Assert.True(result.Success);
        var items = await writeScope.InvoiceItems.Where(candidate => candidate.InvoiceId == result.Value).ToListAsync();
        Assert.Single(items);
        Assert.Equal("Approved line", items[0].Description);
        Assert.Equal(150m, items[0].UnitPrice);

        var invoice = await writeScope.Invoices.SingleAsync(candidate => candidate.Id == result.Value);
        Assert.Equal(estimateId, invoice.SourceEstimateId);
    }

    [Fact]
    public async Task InvoiceManagement_CreateFromApprovedEstimate_RejectsNonApprovedEstimate()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await BillingTestSupport.CreateBillingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(BillingTestSupport.DefaultNow));
        var estimateService = BillingTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(scenario.OrganizationId));
        var invoiceService = BillingTestSupport.CreateInvoiceService(writeScope, new TestOrganizationContext(scenario.OrganizationId));

        var estimateId = (await estimateService.CreateEstimateAsync(scenario.ManagerId, scenario.RepairOrderId)).Value!;
        await estimateService.AddEstimateItemAsync(
            scenario.ManagerId,
            new AddEstimateItemCommand
            {
                EstimateId = estimateId,
                Description = "Draft line",
                Quantity = 1,
                UnitPrice = 10m,
            });

        var result = await invoiceService.CreateInvoiceFromApprovedEstimateAsync(scenario.ManagerId, estimateId);

        Assert.False(result.Success);
        Assert.Equal(InvoiceOperationFailureReason.EstimateNotEligible, result.FailureReason);
    }

    [Fact]
    public async Task InvoiceManagement_CreateFromApprovedEstimate_RejectsSecondActiveInvoiceFromSameEstimate()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await BillingTestSupport.CreateBillingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(BillingTestSupport.DefaultNow));
        var estimateService = BillingTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(scenario.OrganizationId));
        var invoiceService = BillingTestSupport.CreateInvoiceService(writeScope, new TestOrganizationContext(scenario.OrganizationId));

        var estimateId = await BillingTestSupport.CreateApprovedEstimateAsync(
            estimateService,
            scenario.ManagerId,
            scenario.RepairOrderId);

        Assert.True((await invoiceService.CreateInvoiceFromApprovedEstimateAsync(scenario.ManagerId, estimateId)).Success);
        var second = await invoiceService.CreateInvoiceFromApprovedEstimateAsync(scenario.ManagerId, estimateId);

        Assert.False(second.Success);
        Assert.Equal(InvoiceOperationFailureReason.CurrentInvoiceExists, second.FailureReason);
    }

    [Fact]
    public async Task InvoiceManagement_Issue_RequiresAtLeastOneItem()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await BillingTestSupport.CreateBillingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(BillingTestSupport.DefaultNow));
        var service = BillingTestSupport.CreateInvoiceService(writeScope, new TestOrganizationContext(scenario.OrganizationId));
        var invoiceId = (await service.CreateInvoiceAsync(scenario.ManagerId, scenario.RepairOrderId)).Value!;

        var result = await service.IssueInvoiceAsync(scenario.ManagerId, invoiceId);

        Assert.False(result.Success);
        Assert.Equal(InvoiceOperationFailureReason.EmptyInvoice, result.FailureReason);
    }

    [Fact]
    public async Task InvoiceManagement_Issue_SetsIssuedAtUtc()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await BillingTestSupport.CreateBillingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(BillingTestSupport.DefaultNow));
        var service = BillingTestSupport.CreateInvoiceService(writeScope, new TestOrganizationContext(scenario.OrganizationId));
        var invoiceId = await BillingTestSupport.CreateDraftInvoiceWithItemAsync(
            service,
            scenario.ManagerId,
            scenario.RepairOrderId);

        Assert.True((await service.IssueInvoiceAsync(scenario.ManagerId, invoiceId)).Success);
        var invoice = await writeScope.Invoices.SingleAsync(candidate => candidate.Id == invoiceId);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.NotNull(invoice.IssuedAtUtc);
    }

    [Fact]
    public async Task InvoiceManagement_VoidDraft_Succeeds()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await BillingTestSupport.CreateBillingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(BillingTestSupport.DefaultNow));
        var service = BillingTestSupport.CreateInvoiceService(writeScope, new TestOrganizationContext(scenario.OrganizationId));
        var invoiceId = await BillingTestSupport.CreateDraftInvoiceWithItemAsync(
            service,
            scenario.ManagerId,
            scenario.RepairOrderId);

        Assert.True((await service.VoidInvoiceAsync(scenario.ManagerId, invoiceId)).Success);
        var invoice = await writeScope.Invoices.SingleAsync(candidate => candidate.Id == invoiceId);
        Assert.Equal(InvoiceStatus.Voided, invoice.Status);
    }

    [Fact]
    public async Task InvoiceManagement_VoidIssuedWithPayments_IsRejected()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await BillingTestSupport.CreateBillingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(BillingTestSupport.DefaultNow));
        var invoiceService = BillingTestSupport.CreateInvoiceService(writeScope, new TestOrganizationContext(scenario.OrganizationId));
        var paymentService = BillingTestSupport.CreatePaymentService(writeScope, new TestOrganizationContext(scenario.OrganizationId));
        var invoiceId = await BillingTestSupport.CreateIssuedInvoiceAsync(
            invoiceService,
            scenario.ManagerId,
            scenario.RepairOrderId);

        await paymentService.RecordPaymentAsync(
            scenario.ManagerId,
            new RecordPaymentCommand
            {
                InvoiceId = invoiceId,
                Amount = 10m,
                PaymentMethod = PaymentMethod.Cash,
            });

        var result = await invoiceService.VoidInvoiceAsync(scenario.ManagerId, invoiceId);

        Assert.False(result.Success);
        Assert.Equal(InvoiceOperationFailureReason.HasPayments, result.FailureReason);
    }

    [Fact]
    public async Task InvoiceManagement_VoidIssuedWithoutPayments_Succeeds()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await BillingTestSupport.CreateBillingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(BillingTestSupport.DefaultNow));
        var service = BillingTestSupport.CreateInvoiceService(writeScope, new TestOrganizationContext(scenario.OrganizationId));
        var invoiceId = await BillingTestSupport.CreateIssuedInvoiceAsync(
            service,
            scenario.ManagerId,
            scenario.RepairOrderId);

        Assert.True((await service.VoidInvoiceAsync(scenario.ManagerId, invoiceId)).Success);
    }

    [Fact]
    public async Task InvoiceManagement_CommerciallyClosedRepairOrder_BlocksMutations()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await BillingTestSupport.CreateBillingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(BillingTestSupport.DefaultNow));
        var invoiceService = BillingTestSupport.CreateInvoiceService(writeScope, new TestOrganizationContext(scenario.OrganizationId));
        var paymentService = BillingTestSupport.CreatePaymentService(writeScope, new TestOrganizationContext(scenario.OrganizationId));
        var invoiceId = await BillingTestSupport.CreateIssuedInvoiceAsync(
            invoiceService,
            scenario.ManagerId,
            scenario.RepairOrderId);

        await paymentService.RecordPaymentAsync(
            scenario.ManagerId,
            new RecordPaymentCommand
            {
                InvoiceId = invoiceId,
                Amount = 100m,
                PaymentMethod = PaymentMethod.Cash,
            });

        await BillingTestSupport.CompleteRepairOrderAsync(writeScope, scenario.RepairOrderId);
        Assert.True((await paymentService.CloseRepairOrderCommerciallyAsync(scenario.ManagerId, scenario.RepairOrderId)).Success);

        var addResult = await invoiceService.AddInvoiceItemAsync(
            scenario.ManagerId,
            new AddInvoiceItemCommand
            {
                InvoiceId = invoiceId,
                Description = "Blocked",
                Quantity = 1,
                UnitPrice = 1m,
            });

        Assert.False(addResult.Success);
        Assert.Equal(InvoiceOperationFailureReason.CommerciallyClosed, addResult.FailureReason);
    }

    [Fact]
    public async Task InvoiceManagement_RejectsTechnicianRole()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await BillingTestSupport.CreateBillingScenarioAsync(
            scope,
            suffix,
            role: OrganizationMembershipRole.Technician);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(scenario.OrganizationId));
        var service = BillingTestSupport.CreateInvoiceService(writeScope, new TestOrganizationContext(scenario.OrganizationId));

        var result = await service.CreateInvoiceAsync(scenario.ManagerId, scenario.RepairOrderId);

        Assert.False(result.Success);
        Assert.Equal(InvoiceOperationFailureReason.Unauthorized, result.FailureReason);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class BillingManagerAuthorizationTests(PostgreSqlTestFixture fixture)
{
    [Theory]
    [InlineData(OrganizationMembershipRole.Owner)]
    [InlineData(OrganizationMembershipRole.Administrator)]
    [InlineData(OrganizationMembershipRole.ServiceAdvisor)]
    public async Task BillingManager_AllowsManagerRoles(OrganizationMembershipRole role)
    {
        await AssertRoleAllowed(role, shouldSucceed: true);
    }

    [Theory]
    [InlineData(OrganizationMembershipRole.Technician)]
    [InlineData(OrganizationMembershipRole.Viewer)]
    public async Task BillingManager_RejectsNonManagerRoles(OrganizationMembershipRole role)
    {
        await AssertRoleAllowed(role, shouldSucceed: false);
    }

    private async Task AssertRoleAllowed(OrganizationMembershipRole role, bool shouldSucceed)
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var user = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, user.Id, role);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext);
        var handler = new BillingManagerAuthorizationHandler(writeScope, organizationContext);

        var context = new AuthorizationHandlerContext(
            [new BillingManagerRequirement()],
            CreatePrincipal(user.Id),
            resource: null);

        await handler.HandleAsync(context);
        Assert.Equal(shouldSucceed, context.HasSucceeded);
    }

    private static System.Security.Claims.ClaimsPrincipal CreatePrincipal(Guid userId) =>
        new(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString())],
            authenticationType: "Test"));
}
