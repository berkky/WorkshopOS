using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Billing;
using WorkshopOS.Application.CustomerPortal;
using WorkshopOS.Application.Estimates;
using WorkshopOS.Application.Reporting;
using WorkshopOS.Domain.Appointments;
using WorkshopOS.Domain.Billing;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Domain.Staff;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

internal static class ReportingManagementTestHelpers
{
    public const string OrganizationTimeZoneId = "Europe/Istanbul";

    public static DateOnly Today =>
        ReportingPeriodResolver.ResolveTodayInTimeZone(
            ReportingTestSupport.DefaultNow,
            OrganizationTimeZoneId);

    public static ReportingQuery CreateQuery(
        Guid actorUserId,
        DateOnly? from = null,
        DateOnly? to = null,
        Guid? workshopLocationId = null) =>
        new()
        {
            ActorUserId = actorUserId,
            From = from,
            To = to,
            WorkshopLocationId = workshopLocationId,
        };

    public static ClaimsPrincipal CreatePrincipal(Guid userId) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            authenticationType: "Test"));

    public static async Task SetLocationTimeZoneAsync(AppDbContext context, Guid locationId, string timeZoneId)
    {
        var location = await context.WorkshopLocations.SingleAsync(candidate => candidate.Id == locationId);
        context.Entry(location).Property(nameof(WorkshopLocation.TimeZoneId)).CurrentValue = timeZoneId;
        await context.SaveChangesAsync();
    }

    public static async Task<Inspection> PersistInspectionAsync(
        AppDbContext context,
        Guid organizationId,
        Guid repairOrderId,
        InspectionStatus status = InspectionStatus.Draft)
    {
        var inspection = new Inspection(organizationId, repairOrderId, status);
        context.Inspections.Add(inspection);
        await context.SaveChangesAsync();
        return inspection;
    }

    public static async Task SetInvoiceCurrencyAsync(AppDbContext context, Guid invoiceId, string currencyCode)
    {
        var invoice = await context.Invoices.SingleAsync(candidate => candidate.Id == invoiceId);
        context.Entry(invoice).Property(nameof(Invoice.CurrencyCode)).CurrentValue = currencyCode;
        await context.SaveChangesAsync();
    }

    public static async Task<Guid> CreateCommerciallyClosableRepairOrderAsync(
        AppDbContext context,
        Guid organizationId,
        Guid actorUserId,
        Guid locationId,
        Guid customerId,
        Guid vehicleId,
        string suffix,
        decimal invoiceTotal = 100m)
    {
        var repairOrder = await TestDataFactory.PersistRepairOrderAsync(
            context,
            organizationId,
            locationId,
            customerId,
            vehicleId,
            suffix,
            ReportingTestSupport.DefaultNow,
            RepairOrderStatus.Draft);

        var invoiceService = BillingTestSupport.CreateInvoiceService(
            context,
            new TestOrganizationContext(organizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var paymentService = BillingTestSupport.CreatePaymentService(
            context,
            new TestOrganizationContext(organizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));

        var invoiceId = await BillingTestSupport.CreateIssuedInvoiceAsync(
            invoiceService,
            actorUserId,
            repairOrder.Id,
            invoiceTotal);

        await paymentService.RecordPaymentAsync(
            actorUserId,
            new RecordPaymentCommand
            {
                InvoiceId = invoiceId,
                Amount = invoiceTotal,
                PaymentMethod = PaymentMethod.Cash,
            });

        await BillingTestSupport.CompleteRepairOrderAsync(context, repairOrder.Id);
        return repairOrder.Id;
    }

    public static string GetMigrationsDirectory() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "WorkshopOS.Infrastructure", "Persistence", "Migrations"));

    public sealed record DomainRowSnapshot(
        int RepairOrders,
        int Appointments,
        int Inspections,
        int Estimates,
        int Invoices,
        int InvoiceItems,
        int InvoicePaymentRecords,
        int EstimateShares,
        DateTimeOffset? MaxRepairOrderUpdatedAtUtc);
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ReportingViewerAuthorizationTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task ReportingViewer_AllowsOwner() =>
        await AssertRoleAllowed(OrganizationMembershipRole.Owner, shouldSucceed: true);

    [Fact]
    public async Task ReportingViewer_AllowsAdministrator() =>
        await AssertRoleAllowed(OrganizationMembershipRole.Administrator, shouldSucceed: true);

    [Fact]
    public async Task ReportingViewer_AllowsServiceAdvisor() =>
        await AssertRoleAllowed(OrganizationMembershipRole.ServiceAdvisor, shouldSucceed: true);

    [Fact]
    public async Task ReportingViewer_RejectsTechnician() =>
        await AssertRoleAllowed(OrganizationMembershipRole.Technician, shouldSucceed: false);

    [Fact]
    public async Task ReportingViewer_RejectsViewer() =>
        await AssertRoleAllowed(OrganizationMembershipRole.Viewer, shouldSucceed: false);

    [Fact]
    public async Task ReportingViewer_ReflectsDatabaseRoleChange()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var owner = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner");
        var advisor = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-advisor");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            owner.Id,
            OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            advisor.Id,
            OrganizationMembershipRole.ServiceAdvisor);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext);
        var handler = new ReportingViewerAuthorizationHandler(writeScope, organizationContext);

        var initialContext = new AuthorizationHandlerContext(
            [new ReportingViewerRequirement()],
            ReportingManagementTestHelpers.CreatePrincipal(advisor.Id),
            resource: null);
        await handler.HandleAsync(initialContext);
        Assert.True(initialContext.HasSucceeded);

        var advisorMembership = await writeScope.OrganizationMemberships
            .SingleAsync(membership => membership.UserId == advisor.Id);
        advisorMembership.ChangeRole(OrganizationMembershipRole.Technician);
        await writeScope.SaveChangesAsync();

        var afterContext = new AuthorizationHandlerContext(
            [new ReportingViewerRequirement()],
            ReportingManagementTestHelpers.CreatePrincipal(advisor.Id),
            resource: null);
        await handler.HandleAsync(afterContext);
        Assert.False(afterContext.HasSucceeded);
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
        var handler = new ReportingViewerAuthorizationHandler(writeScope, organizationContext);

        var context = new AuthorizationHandlerContext(
            [new ReportingViewerRequirement()],
            ReportingManagementTestHelpers.CreatePrincipal(user.Id),
            resource: null);

        await handler.HandleAsync(context);
        Assert.Equal(shouldSucceed, context.HasSucceeded);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ReportingManagementTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task Reporting_Dashboard_ReturnsOnlyCurrentOrganizationData()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenarioA = await ReportingTestSupport.CreateReportingScenarioAsync(scope, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var actorB = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-b");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationB.Id,
            actorB.Id,
            OrganizationMembershipRole.Owner);

        await using (var scopeB = scope.CreateContext(
            new TestOrganizationContext(organizationB.Id),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow)))
        {
            var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
            var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
            var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id);
            for (var index = 0; index < 3; index++)
            {
                await TestDataFactory.PersistRepairOrderAsync(
                    scopeB,
                    organizationB.Id,
                    locationB.Id,
                    customerB.Id,
                    vehicleB.Id,
                    $"{suffix}-b-{index}",
                    ReportingTestSupport.DefaultNow.AddMinutes(index));
            }
        }

        await using var readScopeA = scope.CreateContext(
            new TestOrganizationContext(scenarioA.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var service = ReportingTestSupport.CreateReportingService(
            readScopeA,
            new TestOrganizationContext(scenarioA.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));

        var result = await service.GetExecutiveDashboardAsync(
            ReportingTestSupport.CreateQuery(scenarioA.ActorUserId));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.CurrentSnapshot.ActiveRepairOrders);
    }

    [Fact]
    public async Task Reporting_Operations_DoesNotCountOtherTenantRepairOrders()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenarioA = await ReportingTestSupport.CreateReportingScenarioAsync(scope, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using (var scopeB = scope.CreateContext(
            new TestOrganizationContext(organizationB.Id),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow)))
        {
            var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
            var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
            var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id);
            await TestDataFactory.PersistRepairOrderAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                customerB.Id,
                vehicleB.Id,
                $"{suffix}-b",
                ReportingTestSupport.DefaultNow);
        }

        await using var readScopeA = scope.CreateContext(
            new TestOrganizationContext(scenarioA.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var service = ReportingTestSupport.CreateReportingService(
            readScopeA,
            new TestOrganizationContext(scenarioA.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));

        var result = await service.GetOperationalReportAsync(
            ReportingManagementTestHelpers.CreateQuery(
                scenarioA.ActorUserId,
                ReportingManagementTestHelpers.Today,
                ReportingManagementTestHelpers.Today));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.SelectedPeriod.RepairOrdersOpened);
    }

    [Fact]
    public async Task Reporting_Commercial_DoesNotCountOtherTenantEstimates()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenarioA = await ReportingTestSupport.CreateReportingScenarioAsync(scope, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var managerB = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-b");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationB.Id,
            managerB.Id,
            OrganizationMembershipRole.Owner);

        await using (var scopeB = scope.CreateContext(
            new TestOrganizationContext(organizationB.Id),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow)))
        {
            var estimateService = EstimateShareTestSupport.CreateEstimateService(
                scopeB,
                new TestOrganizationContext(organizationB.Id),
                new FakeTimeProvider(ReportingTestSupport.DefaultNow));
            var scenarioB = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(
                scopeB,
                organizationB.Id,
                suffix);
            await EstimateShareTestSupport.CreatePresentedEstimateAsync(
                estimateService,
                managerB.Id,
                scenarioB.RepairOrder.Id);
        }

        await using var readScopeA = scope.CreateContext(
            new TestOrganizationContext(scenarioA.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var service = ReportingTestSupport.CreateReportingService(
            readScopeA,
            new TestOrganizationContext(scenarioA.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));

        var result = await service.GetCommercialReportAsync(
            ReportingManagementTestHelpers.CreateQuery(
                scenarioA.ActorUserId,
                ReportingManagementTestHelpers.Today,
                ReportingManagementTestHelpers.Today));

        Assert.True(result.Success);
        Assert.Equal(0, result.Value!.SelectedPeriod.EstimatesPresented);
    }

    [Fact]
    public async Task Reporting_Commercial_DoesNotCountOtherTenantInvoices()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenarioA = await ReportingTestSupport.CreateReportingScenarioAsync(scope, $"{suffix}-a");
        var scenarioB = await BillingTestSupport.CreateBillingScenarioAsync(scope, $"{suffix}-b");

        await using (var scopeB = scope.CreateContext(
            new TestOrganizationContext(scenarioB.OrganizationId),
            new FakeTimeProvider(BillingTestSupport.DefaultNow)))
        {
            var invoiceService = BillingTestSupport.CreateInvoiceService(
                scopeB,
                new TestOrganizationContext(scenarioB.OrganizationId),
                new FakeTimeProvider(BillingTestSupport.DefaultNow));
            await BillingTestSupport.CreateIssuedInvoiceAsync(
                invoiceService,
                scenarioB.ManagerId,
                scenarioB.RepairOrderId);
        }

        await using var readScopeA = scope.CreateContext(
            new TestOrganizationContext(scenarioA.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var service = ReportingTestSupport.CreateReportingService(
            readScopeA,
            new TestOrganizationContext(scenarioA.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));

        var result = await service.GetCommercialReportAsync(
            ReportingManagementTestHelpers.CreateQuery(
                scenarioA.ActorUserId,
                ReportingManagementTestHelpers.Today,
                ReportingManagementTestHelpers.Today));

        Assert.True(result.Success);
        Assert.Equal(0, result.Value!.SelectedPeriod.InvoicesIssued);
    }

    [Fact]
    public async Task Reporting_Commercial_DoesNotCountOtherTenantPayments()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenarioA = await ReportingTestSupport.CreateReportingScenarioAsync(scope, $"{suffix}-a");
        var scenarioB = await BillingTestSupport.CreateBillingScenarioAsync(scope, $"{suffix}-b");

        await using (var scopeB = scope.CreateContext(
            new TestOrganizationContext(scenarioB.OrganizationId),
            new FakeTimeProvider(BillingTestSupport.DefaultNow)))
        {
            var invoiceService = BillingTestSupport.CreateInvoiceService(
                scopeB,
                new TestOrganizationContext(scenarioB.OrganizationId),
                new FakeTimeProvider(BillingTestSupport.DefaultNow));
            var paymentService = BillingTestSupport.CreatePaymentService(
                scopeB,
                new TestOrganizationContext(scenarioB.OrganizationId),
                new FakeTimeProvider(BillingTestSupport.DefaultNow));
            var invoiceId = await BillingTestSupport.CreateIssuedInvoiceAsync(
                invoiceService,
                scenarioB.ManagerId,
                scenarioB.RepairOrderId);
            await paymentService.RecordPaymentAsync(
                scenarioB.ManagerId,
                new RecordPaymentCommand
                {
                    InvoiceId = invoiceId,
                    Amount = 100m,
                    PaymentMethod = PaymentMethod.Cash,
                });
        }

        await using var readScopeA = scope.CreateContext(
            new TestOrganizationContext(scenarioA.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var service = ReportingTestSupport.CreateReportingService(
            readScopeA,
            new TestOrganizationContext(scenarioA.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));

        var result = await service.GetCommercialReportAsync(
            ReportingManagementTestHelpers.CreateQuery(
                scenarioA.ActorUserId,
                ReportingManagementTestHelpers.Today,
                ReportingManagementTestHelpers.Today));

        Assert.True(result.Success);
        Assert.Empty(result.Value!.SelectedPeriod.RecordedPayments);
    }

    [Fact]
    public async Task Reporting_LocationFilter_RejectsOtherTenantLocation()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenarioA = await ReportingTestSupport.CreateReportingScenarioAsync(scope, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid locationBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            locationBId = (await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix)).Id;
        }

        await using var readScopeA = scope.CreateContext(
            new TestOrganizationContext(scenarioA.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var service = ReportingTestSupport.CreateReportingService(
            readScopeA,
            new TestOrganizationContext(scenarioA.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));

        var result = await service.GetExecutiveDashboardAsync(
            ReportingManagementTestHelpers.CreateQuery(
                scenarioA.ActorUserId,
                workshopLocationId: locationBId));

        Assert.False(result.Success);
        Assert.Equal(ReportingFailureReason.LocationNotFound, result.FailureReason);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ReportingDateRangeTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task Reporting_DefaultRange_IsThirtyDaysOrDocumentedEquivalent()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var readScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var service = ReportingTestSupport.CreateReportingService(
            readScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));

        var result = await service.GetExecutiveDashboardAsync(
            ReportingTestSupport.CreateQuery(scenario.ActorUserId));

        Assert.True(result.Success);
        Assert.Equal(ReportingManagementTestHelpers.Today.AddDays(-(ReportingPeriodResolver.DefaultInclusiveDayCount - 1)), result.Value!.Header.From);
        Assert.Equal(ReportingManagementTestHelpers.Today, result.Value.Header.To);
    }

    [Fact]
    public async Task Reporting_RejectsFromAfterTo()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var readScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var service = ReportingTestSupport.CreateReportingService(
            readScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));

        var result = await service.GetExecutiveDashboardAsync(
            ReportingManagementTestHelpers.CreateQuery(
                scenario.ActorUserId,
                ReportingManagementTestHelpers.Today,
                ReportingManagementTestHelpers.Today.AddDays(-1)));

        Assert.False(result.Success);
        Assert.Equal(ReportingFailureReason.InvalidDateRange, result.FailureReason);
    }

    [Fact]
    public async Task Reporting_RejectsRangeOver366Days()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var readScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var service = ReportingTestSupport.CreateReportingService(
            readScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));

        var result = await service.GetExecutiveDashboardAsync(
            ReportingManagementTestHelpers.CreateQuery(
                scenario.ActorUserId,
                ReportingManagementTestHelpers.Today.AddDays(-ReportingPeriodResolver.MaxInclusiveDayCount),
                ReportingManagementTestHelpers.Today));

        Assert.False(result.Success);
        Assert.Equal(ReportingFailureReason.InvalidDateRange, result.FailureReason);
    }

    [Fact]
    public async Task Reporting_UsesInclusiveLocalDates()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;
        var period = new ReportingPeriod(today, today);
        var (startUtc, _) = ReportingPeriodResolver.ConvertToUtcBounds(
            period,
            ReportingManagementTestHelpers.OrganizationTimeZoneId);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, $"{suffix}-appt");
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            startUtc,
            startUtc.AddHours(1));

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetOperationalReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.SelectedPeriod.AppointmentsScheduled);
    }

    [Fact]
    public async Task Reporting_UsesExclusiveUtcEndBoundary()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;
        var period = new ReportingPeriod(today, today);
        var (_, endUtcExclusive) = ReportingPeriodResolver.ConvertToUtcBounds(
            period,
            ReportingManagementTestHelpers.OrganizationTimeZoneId);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, $"{suffix}-appt");
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            endUtcExclusive,
            endUtcExclusive.AddHours(1));

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetOperationalReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Equal(0, result.Value!.SelectedPeriod.AppointmentsScheduled);
    }

    [Fact]
    public async Task Reporting_SelectedLocationUsesLocationTimeZone()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        const string locationTimeZoneId = "America/Los_Angeles";
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        await ReportingManagementTestHelpers.SetLocationTimeZoneAsync(
            writeScope,
            scenario.WorkshopLocationId,
            locationTimeZoneId);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetExecutiveDashboardAsync(
            ReportingManagementTestHelpers.CreateQuery(
                scenario.ActorUserId,
                workshopLocationId: scenario.WorkshopLocationId));

        Assert.True(result.Success);
        Assert.Equal(locationTimeZoneId, result.Value!.Header.TimeZoneId);
    }

    [Fact]
    public async Task Reporting_AllLocationsUsesOrganizationTimeZone()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        await ReportingManagementTestHelpers.SetLocationTimeZoneAsync(
            writeScope,
            scenario.WorkshopLocationId,
            "America/Los_Angeles");

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetExecutiveDashboardAsync(
            ReportingTestSupport.CreateQuery(scenario.ActorUserId));

        Assert.True(result.Success);
        Assert.Equal(ReportingManagementTestHelpers.OrganizationTimeZoneId, result.Value!.Header.TimeZoneId);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ReportingTimezoneTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task Reporting_TimezoneEdge_EventNearUtcMidnight_FallsIntoCorrectBusinessLocalDay()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        // 2026-08-09 21:00 UTC is 2026-08-10 00:00 in Europe/Istanbul.
        var scheduledStartUtc = new DateTimeOffset(2026, 8, 9, 21, 0, 0, TimeSpan.Zero);
        var localDay = DateOnly.FromDateTime(new DateTime(2026, 8, 10));

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, $"{suffix}-appt");
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            scheduledStartUtc,
            scheduledStartUtc.AddHours(1));

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetOperationalReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, localDay, localDay));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.SelectedPeriod.AppointmentsScheduled);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ReportingSnapshotTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task Dashboard_ActiveRepairOrders_CountsOnlyActiveStates()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);

        await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-inprogress",
            ReportingTestSupport.DefaultNow,
            RepairOrderStatus.InProgress);
        await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-completed",
            ReportingTestSupport.DefaultNow.AddMinutes(1),
            RepairOrderStatus.Completed);
        await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-cancelled",
            ReportingTestSupport.DefaultNow.AddMinutes(2),
            RepairOrderStatus.Cancelled);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetExecutiveDashboardAsync(
            ReportingTestSupport.CreateQuery(scenario.ActorUserId));

        Assert.True(result.Success);
        Assert.Equal(2, result.Value!.CurrentSnapshot.ActiveRepairOrders);
    }

    [Fact]
    public async Task Dashboard_UnassignedJobs_UsesActiveAssignmentOnly()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        var assigned = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-assigned",
            ReportingTestSupport.DefaultNow);
        var technician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            suffix);
        var inactiveAssignment = await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.RepairOrderId,
            technician.StaffMember.Id,
            ReportingTestSupport.DefaultNow.AddMinutes(-30),
            ReportingTestSupport.DefaultNow.AddMinutes(-10));
        await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
            writeScope,
            scenario.OrganizationId,
            assigned.Id,
            technician.StaffMember.Id,
            ReportingTestSupport.DefaultNow);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetExecutiveDashboardAsync(
            ReportingTestSupport.CreateQuery(scenario.ActorUserId));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.CurrentSnapshot.UnassignedRepairOrders);
        Assert.NotEqual(Guid.Empty, inactiveAssignment.Id);
    }

    [Fact]
    public async Task Dashboard_UrgentJobs_CountsActiveUrgentOnly()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            "urgent",
            ReportingTestSupport.DefaultNow,
            RepairOrderStatus.Draft,
            RepairOrderPriority.Urgent);
        await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            "urg-done",
            ReportingTestSupport.DefaultNow.AddMinutes(1),
            RepairOrderStatus.Completed,
            RepairOrderPriority.Urgent);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetExecutiveDashboardAsync(
            ReportingTestSupport.CreateQuery(scenario.ActorUserId));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.CurrentSnapshot.UrgentRepairOrders);
    }

    [Fact]
    public async Task Dashboard_TechniciansWorking_UsesInProgressAssignments()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        var inProgressOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-working",
            ReportingTestSupport.DefaultNow,
            RepairOrderStatus.InProgress);
        var assignedOnlyOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-assigned",
            ReportingTestSupport.DefaultNow.AddMinutes(1),
            RepairOrderStatus.InProgress);
        var technicianWorking = await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            $"{suffix}-working");
        var technicianAssigned = await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            $"{suffix}-assigned");
        var workingAssignment = await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
            writeScope,
            scenario.OrganizationId,
            inProgressOrder.Id,
            technicianWorking.StaffMember.Id,
            ReportingTestSupport.DefaultNow);
        workingAssignment.StartWork(ReportingTestSupport.DefaultNow);
        await writeScope.SaveChangesAsync();
        await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
            writeScope,
            scenario.OrganizationId,
            assignedOnlyOrder.Id,
            technicianAssigned.StaffMember.Id,
            ReportingTestSupport.DefaultNow);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetExecutiveDashboardAsync(
            ReportingTestSupport.CreateQuery(scenario.ActorUserId));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.CurrentSnapshot.TechniciansCurrentlyWorking);
    }

    [Fact]
    public async Task Dashboard_OpenInspections_CountsDraftAndInProgress()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        var completedOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-completed",
            ReportingTestSupport.DefaultNow.AddMinutes(1));

        await ReportingManagementTestHelpers.PersistInspectionAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.RepairOrderId,
            InspectionStatus.Draft);
        var inProgressInspection = await ReportingManagementTestHelpers.PersistInspectionAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.RepairOrderId,
            InspectionStatus.Draft);
        inProgressInspection.Start(ReportingTestSupport.DefaultNow);
        await writeScope.SaveChangesAsync();
        var completedInspection = await ReportingManagementTestHelpers.PersistInspectionAsync(
            writeScope,
            scenario.OrganizationId,
            completedOrder.Id,
            InspectionStatus.Draft);
        completedInspection.Start(ReportingTestSupport.DefaultNow);
        completedInspection.Complete(ReportingTestSupport.DefaultNow);
        await writeScope.SaveChangesAsync();

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetExecutiveDashboardAsync(
            ReportingTestSupport.CreateQuery(scenario.ActorUserId));

        Assert.True(result.Success);
        Assert.Equal(2, result.Value!.CurrentSnapshot.OpenDviInspections);
    }

    [Fact]
    public async Task Dashboard_EstimatesAwaitingDecision_CountsSentOnly()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var estimateService = EstimateShareTestSupport.CreateEstimateService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        var otherOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-draft",
            ReportingTestSupport.DefaultNow.AddMinutes(1));

        await EstimateShareTestSupport.CreatePresentedEstimateAsync(
            estimateService,
            scenario.ActorUserId,
            scenario.RepairOrderId);
        var draftEstimateId = (await estimateService.CreateEstimateAsync(scenario.ActorUserId, otherOrder.Id)).Value!;
        await EstimateShareTestSupport.AddEstimateItemAsync(
            estimateService,
            scenario.ActorUserId,
            draftEstimateId,
            "Draft line",
            1,
            50m);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetExecutiveDashboardAsync(
            ReportingTestSupport.CreateQuery(scenario.ActorUserId));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.CurrentSnapshot.EstimatesAwaitingDecision);
    }

    [Fact]
    public async Task Dashboard_OutstandingInvoices_ExcludesPaidAndVoided()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var invoiceService = BillingTestSupport.CreateInvoiceService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var paymentService = BillingTestSupport.CreatePaymentService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        var paidOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-paid",
            ReportingTestSupport.DefaultNow.AddMinutes(1));
        var voidedOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-voided",
            ReportingTestSupport.DefaultNow.AddMinutes(2));

        var outstandingInvoiceId = await BillingTestSupport.CreateIssuedInvoiceAsync(
            invoiceService,
            scenario.ActorUserId,
            scenario.RepairOrderId);
        var paidInvoiceId = await BillingTestSupport.CreateIssuedInvoiceAsync(
            invoiceService,
            scenario.ActorUserId,
            paidOrder.Id);
        await paymentService.RecordPaymentAsync(
            scenario.ActorUserId,
            new RecordPaymentCommand
            {
                InvoiceId = paidInvoiceId,
                Amount = 100m,
                PaymentMethod = PaymentMethod.Cash,
            });
        var voidedInvoiceId = await BillingTestSupport.CreateIssuedInvoiceAsync(
            invoiceService,
            scenario.ActorUserId,
            voidedOrder.Id);
        await invoiceService.VoidInvoiceAsync(scenario.ActorUserId, voidedInvoiceId);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetExecutiveDashboardAsync(
            ReportingTestSupport.CreateQuery(scenario.ActorUserId));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.CurrentSnapshot.OutstandingInvoices);
        Assert.NotEqual(Guid.Empty, outstandingInvoiceId);
    }

    [Fact]
    public async Task Dashboard_CommerciallyClosableJobs_RequiresCompletedAndPaid()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        await ReportingManagementTestHelpers.CreateCommerciallyClosableRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.ActorUserId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-closable");

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetExecutiveDashboardAsync(
            ReportingTestSupport.CreateQuery(scenario.ActorUserId));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.CurrentSnapshot.CommerciallyClosableJobs);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ReportingAppointmentTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task Reporting_Appointments_CountsScheduledStartWithinRange()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;
        var period = new ReportingPeriod(today, today);
        var (startUtc, _) = ReportingPeriodResolver.ConvertToUtcBounds(period, ReportingManagementTestHelpers.OrganizationTimeZoneId);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            startUtc.AddHours(2),
            startUtc.AddHours(3),
            AppointmentStatus.Confirmed);
        await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            startUtc.AddDays(-2),
            startUtc.AddDays(-2).AddHours(1));

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetOperationalReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.SelectedPeriod.AppointmentsScheduled);
    }

    [Fact]
    public async Task Reporting_Appointments_StatusBreakdownIsCorrect()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;
        var period = new ReportingPeriod(today, today);
        var (startUtc, _) = ReportingPeriodResolver.ConvertToUtcBounds(period, ReportingManagementTestHelpers.OrganizationTimeZoneId);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            startUtc.AddHours(1),
            startUtc.AddHours(2),
            AppointmentStatus.Scheduled);
        await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            startUtc.AddHours(3),
            startUtc.AddHours(4),
            AppointmentStatus.Cancelled);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetOperationalReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.SelectedPeriod.AppointmentStatusBreakdown.Scheduled);
        Assert.Equal(1, result.Value.SelectedPeriod.AppointmentStatusBreakdown.Cancelled);
    }

    [Fact]
    public async Task Reporting_Appointments_DoesNotUseCreatedTimestampAsScheduleMetric()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;
        var outsidePeriod = today.AddDays(-10);
        var (outsideStartUtc, _) = ReportingPeriodResolver.ConvertToUtcBounds(
            new ReportingPeriod(outsidePeriod, outsidePeriod),
            ReportingManagementTestHelpers.OrganizationTimeZoneId);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            outsideStartUtc,
            outsideStartUtc.AddHours(1));

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetOperationalReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Equal(0, result.Value!.SelectedPeriod.AppointmentsScheduled);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ReportingRepairOrderTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task Reporting_RepairOrdersOpened_UsesOpenedAtUtc()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;
        var period = new ReportingPeriod(today, today);
        var (startUtc, _) = ReportingPeriodResolver.ConvertToUtcBounds(period, ReportingManagementTestHelpers.OrganizationTimeZoneId);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-opened",
            startUtc.AddHours(1));

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetOperationalReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Equal(2, result.Value!.SelectedPeriod.RepairOrdersOpened);
    }

    [Fact]
    public async Task Reporting_RepairOrdersCompleted_UsesCompletedAtUtc()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;
        var period = new ReportingPeriod(today, today);
        var (startUtc, _) = ReportingPeriodResolver.ConvertToUtcBounds(period, ReportingManagementTestHelpers.OrganizationTimeZoneId);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var repairOrder = await writeScope.RepairOrders.SingleAsync(candidate => candidate.Id == scenario.RepairOrderId);
        repairOrder.StartWork();
        repairOrder.Complete(startUtc.AddHours(2));
        await writeScope.SaveChangesAsync();

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetOperationalReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.SelectedPeriod.RepairOrdersCompleted);
    }

    [Fact]
    public async Task Reporting_CommerciallyClosed_UsesCommerciallyClosedAtUtc()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;
        var period = new ReportingPeriod(today, today);
        var (startUtc, _) = ReportingPeriodResolver.ConvertToUtcBounds(period, ReportingManagementTestHelpers.OrganizationTimeZoneId);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        var repairOrderId = await ReportingManagementTestHelpers.CreateCommerciallyClosableRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.ActorUserId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-close");
        var paymentService = BillingTestSupport.CreatePaymentService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        await paymentService.CloseRepairOrderCommerciallyAsync(scenario.ActorUserId, repairOrderId);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.SelectedPeriod.CommerciallyClosedJobs);
        Assert.NotNull((await writeScope.RepairOrders.SingleAsync(candidate => candidate.Id == repairOrderId)).CommerciallyClosedAtUtc);
    }

    [Fact]
    public async Task Reporting_OperationalCompletionAndCommercialCloseRemainDistinct()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;
        var period = new ReportingPeriod(today, today);
        var (startUtc, _) = ReportingPeriodResolver.ConvertToUtcBounds(period, ReportingManagementTestHelpers.OrganizationTimeZoneId);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var repairOrder = await writeScope.RepairOrders.SingleAsync(candidate => candidate.Id == scenario.RepairOrderId);
        repairOrder.StartWork();
        repairOrder.Complete(startUtc.AddHours(1));
        await writeScope.SaveChangesAsync();

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetOperationalReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.SelectedPeriod.RepairOrdersCompleted);
        Assert.Equal(0, result.Value.SelectedPeriod.CommerciallyClosedJobs);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ReportingInspectionTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task Reporting_InspectionsCompleted_UsesCompletedAtUtc()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;
        var period = new ReportingPeriod(today, today);
        var (startUtc, _) = ReportingPeriodResolver.ConvertToUtcBounds(period, ReportingManagementTestHelpers.OrganizationTimeZoneId);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var inspection = await ReportingManagementTestHelpers.PersistInspectionAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.RepairOrderId);
        inspection.Start(startUtc.AddHours(1));
        inspection.Complete(startUtc.AddHours(2));
        await writeScope.SaveChangesAsync();

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetOperationalReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.SelectedPeriod.InspectionsCompleted);
    }

    [Fact]
    public async Task Reporting_CriticalFindings_CountsCriticalItemsFromCompletedInspections()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;
        var period = new ReportingPeriod(today, today);
        var (startUtc, _) = ReportingPeriodResolver.ConvertToUtcBounds(period, ReportingManagementTestHelpers.OrganizationTimeZoneId);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var inspection = await ReportingManagementTestHelpers.PersistInspectionAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.RepairOrderId);
        inspection.Start(startUtc.AddHours(1));
        inspection.Complete(startUtc.AddHours(2));
        writeScope.InspectionItems.Add(new InspectionItem(
            scenario.OrganizationId,
            inspection.Id,
            "Brakes",
            "Pads",
            InspectionCondition.Critical));
        await writeScope.SaveChangesAsync();

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetOperationalReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.SelectedPeriod.CriticalInspectionFindings);
    }

    [Fact]
    public async Task Reporting_CriticalFindings_DoesNotCountOtherTenantItems()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenarioA = await ReportingTestSupport.CreateReportingScenarioAsync(scope, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using (var scopeB = scope.CreateContext(
            new TestOrganizationContext(organizationB.Id),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow)))
        {
            var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
            var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
            var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id);
            var repairOrderB = await TestDataFactory.PersistRepairOrderAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                customerB.Id,
                vehicleB.Id,
                suffix,
                ReportingTestSupport.DefaultNow);
            var inspectionB = await ReportingManagementTestHelpers.PersistInspectionAsync(
                scopeB,
                organizationB.Id,
                repairOrderB.Id);
            inspectionB.Start(ReportingTestSupport.DefaultNow);
            inspectionB.Complete(ReportingTestSupport.DefaultNow);
            scopeB.InspectionItems.Add(new InspectionItem(
                organizationB.Id,
                inspectionB.Id,
                "Engine",
                "Belt",
                InspectionCondition.Critical));
            await scopeB.SaveChangesAsync();
        }

        await using var readScopeA = scope.CreateContext(
            new TestOrganizationContext(scenarioA.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var service = ReportingTestSupport.CreateReportingService(
            readScopeA,
            new TestOrganizationContext(scenarioA.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetOperationalReportAsync(
            ReportingManagementTestHelpers.CreateQuery(
                scenarioA.ActorUserId,
                ReportingManagementTestHelpers.Today,
                ReportingManagementTestHelpers.Today));

        Assert.True(result.Success);
        Assert.Equal(0, result.Value!.SelectedPeriod.CriticalInspectionFindings);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ReportingEstimateTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task Reporting_EstimatesPresented_UsesSentAtUtc()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var estimateService = EstimateShareTestSupport.CreateEstimateService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        await EstimateShareTestSupport.CreatePresentedEstimateAsync(
            estimateService,
            scenario.ActorUserId,
            scenario.RepairOrderId);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.SelectedPeriod.EstimatesPresented);
    }

    [Fact]
    public async Task Reporting_EstimatesApproved_UsesApprovedAtUtc()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var estimateService = EstimateShareTestSupport.CreateEstimateService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(
            estimateService,
            scenario.ActorUserId,
            scenario.RepairOrderId);
        await estimateService.RecordCustomerApprovalAsync(scenario.ActorUserId, estimateId);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.SelectedPeriod.EstimatesApproved);
    }

    [Fact]
    public async Task Reporting_EstimatesDeclined_UsesDeclinedAtUtc()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var estimateService = EstimateShareTestSupport.CreateEstimateService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(
            estimateService,
            scenario.ActorUserId,
            scenario.RepairOrderId);
        await estimateService.RecordCustomerDeclineAsync(scenario.ActorUserId, estimateId);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.SelectedPeriod.EstimatesDeclined);
    }

    [Fact]
    public async Task Reporting_ApprovalRate_UsesApprovedPlusDeclinedDenominator()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var estimateService = EstimateShareTestSupport.CreateEstimateService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        var approvedOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-approved",
            ReportingTestSupport.DefaultNow.AddMinutes(1));
        var declinedOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-declined",
            ReportingTestSupport.DefaultNow.AddMinutes(2));
        var approvedEstimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(
            estimateService,
            scenario.ActorUserId,
            approvedOrder.Id);
        var declinedEstimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(
            estimateService,
            scenario.ActorUserId,
            declinedOrder.Id);
        await estimateService.RecordCustomerApprovalAsync(scenario.ActorUserId, approvedEstimateId);
        await estimateService.RecordCustomerDeclineAsync(scenario.ActorUserId, declinedEstimateId);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Equal(50.0m, result.Value!.SelectedPeriod.DecisionApprovalRate);
    }

    [Fact]
    public async Task Reporting_ApprovalRate_NoDecisions_ReturnsNoPercentage()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Null(result.Value!.SelectedPeriod.DecisionApprovalRate);
    }

    [Fact]
    public async Task Reporting_ApprovalRate_DoesNotCountPendingSentAsDecision()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var estimateService = EstimateShareTestSupport.CreateEstimateService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        await EstimateShareTestSupport.CreatePresentedEstimateAsync(
            estimateService,
            scenario.ActorUserId,
            scenario.RepairOrderId);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Null(result.Value!.SelectedPeriod.DecisionApprovalRate);
        Assert.Equal(1, result.Value.SelectedPeriod.EstimatesPresented);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ReportingPortalDecisionTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task Reporting_PortalDecisionCount_RequiresEstimateShareDecisionEvidence()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, _, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(manager.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(
            writeScope,
            mutator,
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        await portalService.RecordApprovalAsync(share.PublicId);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(
            ReportingManagementTestHelpers.CreateQuery(manager.ManagerId, today, today));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.DecisionSourceBreakdown!.PortalApprovalsInPeriod);
    }

    [Fact]
    public async Task Reporting_StaffDecisionIsNotMisreportedAsPortalDecision()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var estimateService = EstimateShareTestSupport.CreateEstimateService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(
            estimateService,
            scenario.ActorUserId,
            scenario.RepairOrderId);
        await estimateService.RecordCustomerApprovalAsync(scenario.ActorUserId, estimateId);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Equal(0, result.Value!.DecisionSourceBreakdown!.PortalApprovalsInPeriod);
        Assert.Equal(1, result.Value.DecisionSourceBreakdown.StaffApprovalsInPeriod);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ReportingMoneyTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task Reporting_PaymentsRecorded_GroupsByCurrency()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var invoiceService = BillingTestSupport.CreateInvoiceService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var paymentService = BillingTestSupport.CreatePaymentService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        var tryOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-try",
            ReportingTestSupport.DefaultNow);
        var eurOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-eur",
            ReportingTestSupport.DefaultNow.AddMinutes(1));
        var tryInvoiceId = await BillingTestSupport.CreateIssuedInvoiceAsync(
            invoiceService,
            scenario.ActorUserId,
            tryOrder.Id,
            100m);
        var eurInvoiceId = await BillingTestSupport.CreateIssuedInvoiceAsync(
            invoiceService,
            scenario.ActorUserId,
            eurOrder.Id,
            200m);
        await ReportingManagementTestHelpers.SetInvoiceCurrencyAsync(writeScope, eurInvoiceId, "EUR");
        await paymentService.RecordPaymentAsync(
            scenario.ActorUserId,
            new RecordPaymentCommand { InvoiceId = tryInvoiceId, Amount = 40m, PaymentMethod = PaymentMethod.Cash });
        await paymentService.RecordPaymentAsync(
            scenario.ActorUserId,
            new RecordPaymentCommand { InvoiceId = eurInvoiceId, Amount = 75m, PaymentMethod = PaymentMethod.Card });

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Equal(2, result.Value!.SelectedPeriod.RecordedPayments.Count);
        Assert.Contains(result.Value.SelectedPeriod.RecordedPayments, item => item.CurrencyCode == "TRY" && item.Amount == 40m);
        Assert.Contains(result.Value.SelectedPeriod.RecordedPayments, item => item.CurrencyCode == "EUR" && item.Amount == 75m);
    }

    [Fact]
    public async Task Reporting_OutstandingAmount_GroupsByCurrency()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var invoiceService = BillingTestSupport.CreateInvoiceService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        var eurOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-eur",
            ReportingTestSupport.DefaultNow.AddMinutes(1));
        await BillingTestSupport.CreateIssuedInvoiceAsync(invoiceService, scenario.ActorUserId, scenario.RepairOrderId, 100m);
        var eurInvoiceId = await BillingTestSupport.CreateIssuedInvoiceAsync(
            invoiceService,
            scenario.ActorUserId,
            eurOrder.Id,
            50m);
        await ReportingManagementTestHelpers.SetInvoiceCurrencyAsync(writeScope, eurInvoiceId, "EUR");

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetExecutiveDashboardAsync(
            ReportingTestSupport.CreateQuery(scenario.ActorUserId));

        Assert.True(result.Success);
        Assert.Equal(2, result.Value!.CurrentSnapshot.CurrentOutstandingAmounts.Count);
        Assert.Contains(result.Value.CurrentSnapshot.CurrentOutstandingAmounts, item => item.CurrencyCode == "TRY" && item.Amount == 100m);
        Assert.Contains(result.Value.CurrentSnapshot.CurrentOutstandingAmounts, item => item.CurrencyCode == "EUR" && item.Amount == 50m);
    }

    [Fact]
    public async Task Reporting_DoesNotSumDifferentCurrenciesTogether()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var invoiceService = BillingTestSupport.CreateInvoiceService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        var eurOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-eur",
            ReportingTestSupport.DefaultNow.AddMinutes(1));
        await BillingTestSupport.CreateIssuedInvoiceAsync(invoiceService, scenario.ActorUserId, scenario.RepairOrderId, 100m);
        var eurInvoiceId = await BillingTestSupport.CreateIssuedInvoiceAsync(
            invoiceService,
            scenario.ActorUserId,
            eurOrder.Id,
            50m);
        await ReportingManagementTestHelpers.SetInvoiceCurrencyAsync(writeScope, eurInvoiceId, "EUR");

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Equal(2, result.Value!.SelectedPeriod.InvoicedAmounts.Count);
        Assert.DoesNotContain(result.Value.SelectedPeriod.InvoicedAmounts, item => item.Amount == 150m);
    }

    [Fact]
    public async Task Reporting_PaymentAmount_UsesRecordedAtUtcPeriod()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;
        var outsideDay = today.AddDays(-5);
        var (outsideStartUtc, _) = ReportingPeriodResolver.ConvertToUtcBounds(
            new ReportingPeriod(outsideDay, outsideDay),
            ReportingManagementTestHelpers.OrganizationTimeZoneId);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(outsideStartUtc.AddHours(12)));
        var invoiceService = BillingTestSupport.CreateInvoiceService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(outsideStartUtc.AddHours(12)));
        var paymentService = BillingTestSupport.CreatePaymentService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(outsideStartUtc.AddHours(12)));
        var invoiceId = await BillingTestSupport.CreateIssuedInvoiceAsync(
            invoiceService,
            scenario.ActorUserId,
            scenario.RepairOrderId,
            100m);
        await paymentService.RecordPaymentAsync(
            scenario.ActorUserId,
            new RecordPaymentCommand { InvoiceId = invoiceId, Amount = 25m, PaymentMethod = PaymentMethod.Cash });

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Empty(result.Value!.SelectedPeriod.RecordedPayments);
    }

    [Fact]
    public async Task Reporting_InvoiceAmount_UsesInvoiceSnapshotLines()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var invoiceService = BillingTestSupport.CreateInvoiceService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var invoiceId = await BillingTestSupport.CreateDraftInvoiceWithItemAsync(
            invoiceService,
            scenario.ActorUserId,
            scenario.RepairOrderId,
            description: "Rounding line",
            quantity: 6.25m,
            unitPrice: 4.96m);
        await invoiceService.IssueInvoiceAsync(scenario.ActorUserId, invoiceId);
        var lineItems = await writeScope.InvoiceItems
            .Where(candidate => candidate.InvoiceId == invoiceId)
            .Select(candidate => new { candidate.Quantity, candidate.UnitPrice })
            .ToListAsync();
        var expectedTotal = InvoiceMoneyCalculator.CalculateInvoiceTotal(
            lineItems.Select(item => (item.Quantity, item.UnitPrice)).ToList());

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Single(result.Value!.SelectedPeriod.InvoicedAmounts);
        Assert.Equal(expectedTotal, result.Value.SelectedPeriod.InvoicedAmounts[0].Amount);
        Assert.Equal(31.00m, expectedTotal);
        Assert.Equal(31.00m, InvoiceMoneyCalculator.CalculateLineTotal(3m, 10.333m));
    }

    [Fact]
    public async Task Reporting_InvoiceAmount_ObeysAuthoritativeMoneyRounding()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var invoiceService = BillingTestSupport.CreateInvoiceService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var invoiceId = await BillingTestSupport.CreateDraftInvoiceWithItemAsync(
            invoiceService,
            scenario.ActorUserId,
            scenario.RepairOrderId,
            description: "Rounding line",
            quantity: 6.25m,
            unitPrice: 4.96m);
        await invoiceService.IssueInvoiceAsync(scenario.ActorUserId, invoiceId);
        var lineItems = await writeScope.InvoiceItems
            .Where(candidate => candidate.InvoiceId == invoiceId)
            .Select(candidate => new { candidate.Quantity, candidate.UnitPrice })
            .ToListAsync();
        var calculatorTotal = InvoiceMoneyCalculator.CalculateInvoiceTotal(
            lineItems.Select(item => (item.Quantity, item.UnitPrice)).ToList());

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(
            ReportingManagementTestHelpers.CreateQuery(scenario.ActorUserId, today, today));

        Assert.True(result.Success);
        Assert.Equal(calculatorTotal, result.Value!.SelectedPeriod.InvoicedAmounts.Single().Amount);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ReportingPaymentStateTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task Reporting_CurrentInvoiceState_UnpaidCorrect()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var invoiceService = BillingTestSupport.CreateInvoiceService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        await BillingTestSupport.CreateIssuedInvoiceAsync(invoiceService, scenario.ActorUserId, scenario.RepairOrderId);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(ReportingTestSupport.CreateQuery(scenario.ActorUserId));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.CurrentInvoicePaymentStateBreakdown.Unpaid);
    }

    [Fact]
    public async Task Reporting_CurrentInvoiceState_PartiallyPaidCorrect()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var invoiceService = BillingTestSupport.CreateInvoiceService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var paymentService = BillingTestSupport.CreatePaymentService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var invoiceId = await BillingTestSupport.CreateIssuedInvoiceAsync(
            invoiceService,
            scenario.ActorUserId,
            scenario.RepairOrderId);
        await paymentService.RecordPaymentAsync(
            scenario.ActorUserId,
            new RecordPaymentCommand { InvoiceId = invoiceId, Amount = 25m, PaymentMethod = PaymentMethod.Cash });

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(ReportingTestSupport.CreateQuery(scenario.ActorUserId));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.CurrentInvoicePaymentStateBreakdown.PartiallyPaid);
    }

    [Fact]
    public async Task Reporting_CurrentInvoiceState_PaidCorrect()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var invoiceService = BillingTestSupport.CreateInvoiceService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var paymentService = BillingTestSupport.CreatePaymentService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var invoiceId = await BillingTestSupport.CreateIssuedInvoiceAsync(
            invoiceService,
            scenario.ActorUserId,
            scenario.RepairOrderId);
        await paymentService.RecordPaymentAsync(
            scenario.ActorUserId,
            new RecordPaymentCommand { InvoiceId = invoiceId, Amount = 100m, PaymentMethod = PaymentMethod.Cash });

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(ReportingTestSupport.CreateQuery(scenario.ActorUserId));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.CurrentInvoicePaymentStateBreakdown.Paid);
    }

    [Fact]
    public async Task Reporting_VoidedInvoiceExcludedFromCurrentPaymentState()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var invoiceService = BillingTestSupport.CreateInvoiceService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        var voidedOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-voided",
            ReportingTestSupport.DefaultNow.AddMinutes(1));
        await BillingTestSupport.CreateIssuedInvoiceAsync(invoiceService, scenario.ActorUserId, scenario.RepairOrderId);
        var voidedInvoiceId = await BillingTestSupport.CreateIssuedInvoiceAsync(
            invoiceService,
            scenario.ActorUserId,
            voidedOrder.Id);
        await invoiceService.VoidInvoiceAsync(scenario.ActorUserId, voidedInvoiceId);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(ReportingTestSupport.CreateQuery(scenario.ActorUserId));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.CurrentInvoicePaymentStateBreakdown.Unpaid);
        Assert.Equal(0, result.Value.CurrentInvoicePaymentStateBreakdown.PartiallyPaid + result.Value.CurrentInvoicePaymentStateBreakdown.Paid);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ReportingTechnicianWorkloadTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task Reporting_TechnicianWorkload_IsTenantScoped()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenarioA = await ReportingTestSupport.CreateReportingScenarioAsync(scope, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using (var scopeB = scope.CreateContext(
            new TestOrganizationContext(organizationB.Id),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow)))
        {
            var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
            var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
            var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id);
            var repairOrderB = await TestDataFactory.PersistRepairOrderAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                customerB.Id,
                vehicleB.Id,
                suffix,
                ReportingTestSupport.DefaultNow);
            var technicianB = await TestDataFactory.PersistTechnicianAtLocationAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                suffix);
            await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
                scopeB,
                organizationB.Id,
                repairOrderB.Id,
                technicianB.StaffMember.Id,
                ReportingTestSupport.DefaultNow);
        }

        await using var readScopeA = scope.CreateContext(
            new TestOrganizationContext(scenarioA.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var service = ReportingTestSupport.CreateReportingService(
            readScopeA,
            new TestOrganizationContext(scenarioA.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetOperationalReportAsync(
            ReportingTestSupport.CreateQuery(scenarioA.ActorUserId));

        Assert.True(result.Success);
        Assert.Empty(result.Value!.TechnicianWorkload);
    }

    [Fact]
    public async Task Reporting_TechnicianWorkload_ActiveAssignmentsCorrect()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        var secondOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            $"{suffix}-second",
            ReportingTestSupport.DefaultNow.AddMinutes(1));
        var technician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            suffix);
        await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.RepairOrderId,
            technician.StaffMember.Id,
            ReportingTestSupport.DefaultNow);
        await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
            writeScope,
            scenario.OrganizationId,
            secondOrder.Id,
            technician.StaffMember.Id,
            ReportingTestSupport.DefaultNow.AddMinutes(1));

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetOperationalReportAsync(
            ReportingTestSupport.CreateQuery(scenario.ActorUserId));

        Assert.True(result.Success);
        var workload = Assert.Single(result.Value!.TechnicianWorkload);
        Assert.Equal(2, workload.ActiveAssignedJobs);
    }

    [Fact]
    public async Task Reporting_TechnicianWorkload_InProgressCorrect()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var technician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            suffix);
        var assignment = await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.RepairOrderId,
            technician.StaffMember.Id,
            ReportingTestSupport.DefaultNow);
        assignment.StartWork(ReportingTestSupport.DefaultNow);
        await writeScope.SaveChangesAsync();

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetOperationalReportAsync(
            ReportingTestSupport.CreateQuery(scenario.ActorUserId));

        Assert.True(result.Success);
        var workload = Assert.Single(result.Value!.TechnicianWorkload);
        Assert.Equal(1, workload.InProgressJobs);
    }

    [Fact]
    public async Task Reporting_TechnicianWorkload_DoesNotTreatStaffPositionAsAuthorization()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        await TestDataFactory.PersistStaffMemberAsync(
            writeScope,
            scenario.OrganizationId,
            $"{suffix}-advisor",
            scenario.ActorUserId,
            StaffPosition.ServiceAdvisor);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetOperationalReportAsync(
            ReportingTestSupport.CreateQuery(scenario.ActorUserId));

        Assert.True(result.Success);
        Assert.Empty(result.Value!.TechnicianWorkload);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ReportingLocationTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task Reporting_LocationBreakdown_IsTenantScoped()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenarioA = await ReportingTestSupport.CreateReportingScenarioAsync(scope, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
        }

        await using var readScopeA = scope.CreateContext(
            new TestOrganizationContext(scenarioA.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var service = ReportingTestSupport.CreateReportingService(
            readScopeA,
            new TestOrganizationContext(scenarioA.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetOperationalReportAsync(
            ReportingTestSupport.CreateQuery(scenarioA.ActorUserId));

        Assert.True(result.Success);
        Assert.Single(result.Value!.LocationBreakdown);
    }

    [Fact]
    public async Task Reporting_LocationFilter_AppliesToRepairOrders()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var locationTwo = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, scenario.OrganizationId, $"{suffix}-2");
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            locationTwo.Id,
            customer.Id,
            vehicle.Id,
            $"{suffix}-other",
            ReportingTestSupport.DefaultNow.AddMinutes(1));

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetExecutiveDashboardAsync(
            ReportingManagementTestHelpers.CreateQuery(
                scenario.ActorUserId,
                workshopLocationId: scenario.WorkshopLocationId));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.CurrentSnapshot.ActiveRepairOrders);
    }

    [Fact]
    public async Task Reporting_LocationFilter_AppliesToAppointments()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;
        var period = new ReportingPeriod(today, today);
        var (startUtc, _) = ReportingPeriodResolver.ConvertToUtcBounds(period, ReportingManagementTestHelpers.OrganizationTimeZoneId);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var locationTwo = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, scenario.OrganizationId, $"{suffix}-2");
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.WorkshopLocationId,
            customer.Id,
            vehicle.Id,
            startUtc.AddHours(1),
            startUtc.AddHours(2));
        await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            scenario.OrganizationId,
            locationTwo.Id,
            customer.Id,
            vehicle.Id,
            startUtc.AddHours(3),
            startUtc.AddHours(4));

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetOperationalReportAsync(
            ReportingManagementTestHelpers.CreateQuery(
                scenario.ActorUserId,
                today,
                today,
                scenario.WorkshopLocationId));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.SelectedPeriod.AppointmentsScheduled);
    }

    [Fact]
    public async Task Reporting_LocationFilter_AppliesToInspections()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var locationTwo = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, scenario.OrganizationId, $"{suffix}-2");
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        var otherOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            locationTwo.Id,
            customer.Id,
            vehicle.Id,
            $"{suffix}-other",
            ReportingTestSupport.DefaultNow.AddMinutes(1));
        await ReportingManagementTestHelpers.PersistInspectionAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.RepairOrderId);
        await ReportingManagementTestHelpers.PersistInspectionAsync(
            writeScope,
            scenario.OrganizationId,
            otherOrder.Id);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetExecutiveDashboardAsync(
            ReportingManagementTestHelpers.CreateQuery(
                scenario.ActorUserId,
                workshopLocationId: scenario.WorkshopLocationId));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.CurrentSnapshot.OpenDviInspections);
    }

    [Fact]
    public async Task Reporting_LocationFilter_AppliesToEstimates()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var estimateService = EstimateShareTestSupport.CreateEstimateService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var locationTwo = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, scenario.OrganizationId, $"{suffix}-2");
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        var otherOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            locationTwo.Id,
            customer.Id,
            vehicle.Id,
            $"{suffix}-other",
            ReportingTestSupport.DefaultNow.AddMinutes(1));
        await EstimateShareTestSupport.CreatePresentedEstimateAsync(
            estimateService,
            scenario.ActorUserId,
            scenario.RepairOrderId);
        await EstimateShareTestSupport.CreatePresentedEstimateAsync(
            estimateService,
            scenario.ActorUserId,
            otherOrder.Id);

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(
            ReportingManagementTestHelpers.CreateQuery(
                scenario.ActorUserId,
                today,
                today,
                scenario.WorkshopLocationId));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.SelectedPeriod.EstimatesPresented);
    }

    [Fact]
    public async Task Reporting_LocationFilter_AppliesToInvoicesAndPayments()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);
        var today = ReportingManagementTestHelpers.Today;

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var invoiceService = BillingTestSupport.CreateInvoiceService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var paymentService = BillingTestSupport.CreatePaymentService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var locationTwo = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, scenario.OrganizationId, $"{suffix}-2");
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        var otherOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            locationTwo.Id,
            customer.Id,
            vehicle.Id,
            $"{suffix}-other",
            ReportingTestSupport.DefaultNow.AddMinutes(1));
        var filteredInvoiceId = await BillingTestSupport.CreateIssuedInvoiceAsync(
            invoiceService,
            scenario.ActorUserId,
            scenario.RepairOrderId);
        var otherInvoiceId = await BillingTestSupport.CreateIssuedInvoiceAsync(
            invoiceService,
            scenario.ActorUserId,
            otherOrder.Id);
        await paymentService.RecordPaymentAsync(
            scenario.ActorUserId,
            new RecordPaymentCommand { InvoiceId = filteredInvoiceId, Amount = 10m, PaymentMethod = PaymentMethod.Cash });
        await paymentService.RecordPaymentAsync(
            scenario.ActorUserId,
            new RecordPaymentCommand { InvoiceId = otherInvoiceId, Amount = 20m, PaymentMethod = PaymentMethod.Cash });

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var result = await service.GetCommercialReportAsync(
            ReportingManagementTestHelpers.CreateQuery(
                scenario.ActorUserId,
                today,
                today,
                scenario.WorkshopLocationId));

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.SelectedPeriod.InvoicesIssued);
        Assert.Single(result.Value.SelectedPeriod.RecordedPayments);
        Assert.Equal(10m, result.Value.SelectedPeriod.RecordedPayments[0].Amount);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class CustomerPortalReportingTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task CustomerPortalAuthentication_DoesNotAuthorizeDashboard()
    {
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var service = ReportingTestSupport.CreateReportingService(
            scope.Context,
            new UnresolvedOrganizationContext(),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));

        var result = await service.GetExecutiveDashboardAsync(
            ReportingTestSupport.CreateQuery(Guid.CreateVersion7()));

        Assert.False(result.Success);
        Assert.Equal(ReportingFailureReason.OrganizationNotResolved, result.FailureReason);
    }

    [Fact]
    public async Task CustomerPortalAuthentication_DoesNotAuthorizeOperationsReport()
    {
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var service = ReportingTestSupport.CreateReportingService(
            scope.Context,
            new UnresolvedOrganizationContext(),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));

        var result = await service.GetOperationalReportAsync(
            ReportingTestSupport.CreateQuery(Guid.CreateVersion7()));

        Assert.False(result.Success);
        Assert.Equal(ReportingFailureReason.OrganizationNotResolved, result.FailureReason);
    }

    [Fact]
    public async Task CustomerPortalAuthentication_DoesNotAuthorizeCommercialReport()
    {
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var service = ReportingTestSupport.CreateReportingService(
            scope.Context,
            new UnresolvedOrganizationContext(),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));

        var result = await service.GetCommercialReportAsync(
            ReportingTestSupport.CreateQuery(Guid.CreateVersion7()));

        Assert.False(result.Success);
        Assert.Equal(ReportingFailureReason.OrganizationNotResolved, result.FailureReason);
    }

    [Fact]
    public void CustomerPortalProjection_DoesNotExposeReportingMetrics()
    {
        var properties = typeof(CustomerEstimatePortalDetails)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        Assert.DoesNotContain(properties, name => name.Contains("Reporting", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Dashboard", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Kpi", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Snapshot", StringComparison.OrdinalIgnoreCase));
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ReportingReadOnlyTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task Reporting_ReadOnly_DoesNotMutateDomainData()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await ReportingTestSupport.CreateReportingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var estimateService = EstimateShareTestSupport.CreateEstimateService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        await EstimateShareTestSupport.CreatePresentedEstimateAsync(
            estimateService,
            scenario.ActorUserId,
            scenario.RepairOrderId);
        await ReportingManagementTestHelpers.PersistInspectionAsync(
            writeScope,
            scenario.OrganizationId,
            scenario.RepairOrderId);

        var before = new ReportingManagementTestHelpers.DomainRowSnapshot(
            await writeScope.RepairOrders.CountAsync(),
            await writeScope.Appointments.CountAsync(),
            await writeScope.Inspections.CountAsync(),
            await writeScope.Estimates.CountAsync(),
            await writeScope.Invoices.CountAsync(),
            await writeScope.InvoiceItems.CountAsync(),
            await writeScope.InvoicePaymentRecords.CountAsync(),
            await writeScope.EstimateShares.CountAsync(),
            await writeScope.RepairOrders.MaxAsync(candidate => (DateTimeOffset?)candidate.UpdatedAtUtc));

        var service = ReportingTestSupport.CreateReportingService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(ReportingTestSupport.DefaultNow));
        var query = ReportingTestSupport.CreateQuery(scenario.ActorUserId);

        Assert.True((await service.GetExecutiveDashboardAsync(query)).Success);
        Assert.True((await service.GetOperationalReportAsync(query)).Success);
        Assert.True((await service.GetCommercialReportAsync(query)).Success);

        var after = new ReportingManagementTestHelpers.DomainRowSnapshot(
            await writeScope.RepairOrders.CountAsync(),
            await writeScope.Appointments.CountAsync(),
            await writeScope.Inspections.CountAsync(),
            await writeScope.Estimates.CountAsync(),
            await writeScope.Invoices.CountAsync(),
            await writeScope.InvoiceItems.CountAsync(),
            await writeScope.InvoicePaymentRecords.CountAsync(),
            await writeScope.EstimateShares.CountAsync(),
            await writeScope.RepairOrders.MaxAsync(candidate => (DateTimeOffset?)candidate.UpdatedAtUtc));

        Assert.Equal(before, after);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ReportingMigrationTests
{
    [Fact]
    public void Reporting_HasNoStep19Migration()
    {
        var migrationFiles = Directory.GetFiles(
                ReportingManagementTestHelpers.GetMigrationsDirectory(),
                "*.cs")
            .Where(path =>
                !path.EndsWith("Designer.cs", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith("AppDbContextModelSnapshot.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(9, migrationFiles.Count);
        Assert.DoesNotContain(
            migrationFiles,
            path => Path.GetFileName(path).Contains("Reporting", StringComparison.OrdinalIgnoreCase));
    }
}
