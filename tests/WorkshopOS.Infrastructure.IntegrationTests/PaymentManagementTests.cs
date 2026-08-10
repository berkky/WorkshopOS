using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Billing;
using WorkshopOS.Domain.Billing;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PaymentManagementTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task PaymentManagement_RecordPayment_OnIssuedInvoice_Succeeds()
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
            scenario.RepairOrderId,
            totalUnitPrice: 100m);

        var result = await paymentService.RecordPaymentAsync(
            scenario.ManagerId,
            new RecordPaymentCommand
            {
                InvoiceId = invoiceId,
                Amount = 40m,
                PaymentMethod = PaymentMethod.Card,
                Reference = "AUTH-1",
            });

        Assert.True(result.Success);
        var payment = await writeScope.InvoicePaymentRecords.SingleAsync(candidate => candidate.Id == result.Value);
        Assert.Equal(40m, payment.Amount);
        Assert.Equal(PaymentMethod.Card, payment.PaymentMethod);
    }

    [Fact]
    public async Task PaymentManagement_RecordPayment_RejectsDraftInvoice()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await BillingTestSupport.CreateBillingScenarioAsync(scope, suffix);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(scenario.OrganizationId),
            new FakeTimeProvider(BillingTestSupport.DefaultNow));
        var invoiceService = BillingTestSupport.CreateInvoiceService(writeScope, new TestOrganizationContext(scenario.OrganizationId));
        var paymentService = BillingTestSupport.CreatePaymentService(writeScope, new TestOrganizationContext(scenario.OrganizationId));
        var invoiceId = await BillingTestSupport.CreateDraftInvoiceWithItemAsync(
            invoiceService,
            scenario.ManagerId,
            scenario.RepairOrderId);

        var result = await paymentService.RecordPaymentAsync(
            scenario.ManagerId,
            new RecordPaymentCommand
            {
                InvoiceId = invoiceId,
                Amount = 10m,
                PaymentMethod = PaymentMethod.Cash,
            });

        Assert.False(result.Success);
        Assert.Equal(PaymentOperationFailureReason.InvalidLifecycleTransition, result.FailureReason);
    }

    [Fact]
    public async Task PaymentManagement_RecordPayment_RejectsOverpayment()
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
            scenario.RepairOrderId,
            totalUnitPrice: 100m);

        var result = await paymentService.RecordPaymentAsync(
            scenario.ManagerId,
            new RecordPaymentCommand
            {
                InvoiceId = invoiceId,
                Amount = 100.01m,
                PaymentMethod = PaymentMethod.Cash,
            });

        Assert.False(result.Success);
        Assert.Equal(PaymentOperationFailureReason.Overpayment, result.FailureReason);
    }

    [Fact]
    public async Task PaymentManagement_RecordPayment_RejectsZeroAmount()
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

        var result = await paymentService.RecordPaymentAsync(
            scenario.ManagerId,
            new RecordPaymentCommand
            {
                InvoiceId = invoiceId,
                Amount = 0m,
                PaymentMethod = PaymentMethod.Cash,
            });

        Assert.False(result.Success);
        Assert.Equal(PaymentOperationFailureReason.InvalidInput, result.FailureReason);
    }

    [Fact]
    public async Task PaymentManagement_CommercialClose_RequiresCompletedRepairOrder()
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

        var result = await paymentService.CloseRepairOrderCommerciallyAsync(scenario.ManagerId, scenario.RepairOrderId);

        Assert.False(result.Success);
        Assert.Equal(InvoiceOperationFailureReason.RepairOrderNotCompleted, result.FailureReason);
    }

    [Fact]
    public async Task PaymentManagement_CommercialClose_RequiresFullyPaidInvoice()
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
            scenario.RepairOrderId,
            totalUnitPrice: 100m);

        await paymentService.RecordPaymentAsync(
            scenario.ManagerId,
            new RecordPaymentCommand
            {
                InvoiceId = invoiceId,
                Amount = 50m,
                PaymentMethod = PaymentMethod.Cash,
            });

        await BillingTestSupport.CompleteRepairOrderAsync(writeScope, scenario.RepairOrderId);

        var result = await paymentService.CloseRepairOrderCommerciallyAsync(scenario.ManagerId, scenario.RepairOrderId);

        Assert.False(result.Success);
        Assert.Equal(InvoiceOperationFailureReason.InvoiceNotPaid, result.FailureReason);
    }

    [Fact]
    public async Task PaymentManagement_CommercialClose_SucceedsWhenEligible()
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
            scenario.RepairOrderId,
            totalUnitPrice: 100m);

        await paymentService.RecordPaymentAsync(
            scenario.ManagerId,
            new RecordPaymentCommand
            {
                InvoiceId = invoiceId,
                Amount = 100m,
                PaymentMethod = PaymentMethod.Cash,
            });

        await BillingTestSupport.CompleteRepairOrderAsync(writeScope, scenario.RepairOrderId);

        var result = await paymentService.CloseRepairOrderCommerciallyAsync(scenario.ManagerId, scenario.RepairOrderId);

        Assert.True(result.Success);
        var repairOrder = await writeScope.RepairOrders.SingleAsync(candidate => candidate.Id == scenario.RepairOrderId);
        Assert.NotNull(repairOrder.CommerciallyClosedAtUtc);
    }

    [Fact]
    public async Task PaymentManagement_CommercialClose_RejectsAlreadyClosed()
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

        var second = await paymentService.CloseRepairOrderCommerciallyAsync(scenario.ManagerId, scenario.RepairOrderId);

        Assert.False(second.Success);
        Assert.Equal(InvoiceOperationFailureReason.AlreadyCommerciallyClosed, second.FailureReason);
    }

    [Fact]
    public async Task PaymentManagement_RecordPayment_BlockedWhenCommerciallyClosed()
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
            scenario.RepairOrderId,
            totalUnitPrice: 200m);

        await paymentService.RecordPaymentAsync(
            scenario.ManagerId,
            new RecordPaymentCommand
            {
                InvoiceId = invoiceId,
                Amount = 100m,
                PaymentMethod = PaymentMethod.Cash,
            });

        await BillingTestSupport.CompleteRepairOrderAsync(writeScope, scenario.RepairOrderId);
        await paymentService.RecordPaymentAsync(
            scenario.ManagerId,
            new RecordPaymentCommand
            {
                InvoiceId = invoiceId,
                Amount = 100m,
                PaymentMethod = PaymentMethod.Cash,
            });
        await paymentService.CloseRepairOrderCommerciallyAsync(scenario.ManagerId, scenario.RepairOrderId);

        var result = await paymentService.RecordPaymentAsync(
            scenario.ManagerId,
            new RecordPaymentCommand
            {
                InvoiceId = invoiceId,
                Amount = 1m,
                PaymentMethod = PaymentMethod.Cash,
            });

        Assert.False(result.Success);
        Assert.Equal(PaymentOperationFailureReason.CommerciallyClosed, result.FailureReason);
    }
}
