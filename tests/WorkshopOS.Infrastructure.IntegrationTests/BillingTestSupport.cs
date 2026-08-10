using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Billing;
using WorkshopOS.Application.Estimates;
using WorkshopOS.Domain.Billing;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Infrastructure.Billing;
using WorkshopOS.Infrastructure.Estimates;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

internal static class BillingTestSupport
{
    public static readonly DateTimeOffset DefaultNow = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);

    public static IInvoiceManagementService CreateInvoiceService(
        AppDbContext context,
        IOrganizationContext organizationContext,
        TimeProvider? timeProvider = null) =>
        new InvoiceManagementService(
            context,
            organizationContext,
            new InvoiceNumberGenerator(),
            timeProvider ?? new FakeTimeProvider(DefaultNow));

    public static IPaymentManagementService CreatePaymentService(
        AppDbContext context,
        IOrganizationContext organizationContext,
        TimeProvider? timeProvider = null) =>
        new PaymentManagementService(
            context,
            organizationContext,
            timeProvider ?? new FakeTimeProvider(DefaultNow));

    public static IEstimateManagementService CreateEstimateService(
        AppDbContext context,
        IOrganizationContext organizationContext,
        TimeProvider? timeProvider = null) =>
        new EstimateManagementService(
            context,
            organizationContext,
            new EstimateNumberGenerator(),
            timeProvider ?? new FakeTimeProvider(DefaultNow));

    public static async Task<BillingScenario> CreateBillingScenarioAsync(
        DatabaseTransactionScope scope,
        string suffix,
        OrganizationMembershipRole role = OrganizationMembershipRole.Owner,
        RepairOrderStatus repairOrderStatus = RepairOrderStatus.Draft)
    {
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, role);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(organization.Id),
            new FakeTimeProvider(DefaultNow));

        var location = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, organization.Id, suffix);
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, organization.Id, suffix, customer.Id);
        var repairOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            organization.Id,
            location.Id,
            customer.Id,
            vehicle.Id,
            suffix,
            DefaultNow,
            repairOrderStatus);

        return new BillingScenario(organization.Id, manager.Id, repairOrder.Id);
    }

    public static async Task<Guid> CreateDraftInvoiceWithItemAsync(
        IInvoiceManagementService invoiceService,
        Guid actorUserId,
        Guid repairOrderId,
        string description = "Service line",
        decimal quantity = 1m,
        decimal unitPrice = 100m)
    {
        var invoiceId = (await invoiceService.CreateInvoiceAsync(actorUserId, repairOrderId)).Value!;
        await invoiceService.AddInvoiceItemAsync(
            actorUserId,
            new AddInvoiceItemCommand
            {
                InvoiceId = invoiceId,
                Description = description,
                Quantity = quantity,
                UnitPrice = unitPrice,
            });

        return invoiceId;
    }

    public static async Task<Guid> CreateIssuedInvoiceAsync(
        IInvoiceManagementService invoiceService,
        Guid actorUserId,
        Guid repairOrderId,
        decimal totalUnitPrice = 100m)
    {
        var invoiceId = await CreateDraftInvoiceWithItemAsync(
            invoiceService,
            actorUserId,
            repairOrderId,
            unitPrice: totalUnitPrice);

        await invoiceService.IssueInvoiceAsync(actorUserId, invoiceId);
        return invoiceId;
    }

    public static async Task<Guid> CreateApprovedEstimateAsync(
        IEstimateManagementService estimateService,
        Guid actorUserId,
        Guid repairOrderId)
    {
        var estimateId = (await estimateService.CreateEstimateAsync(actorUserId, repairOrderId)).Value!;
        await estimateService.AddEstimateItemAsync(
            actorUserId,
            new AddEstimateItemCommand
            {
                EstimateId = estimateId,
                Description = "Approved line",
                Quantity = 1,
                UnitPrice = 150m,
            });
        await estimateService.PresentForApprovalAsync(actorUserId, estimateId);
        await estimateService.RecordCustomerApprovalAsync(actorUserId, estimateId);
        return estimateId;
    }

    public static async Task CompleteRepairOrderAsync(AppDbContext context, Guid repairOrderId)
    {
        var repairOrder = await context.RepairOrders.SingleAsync(candidate => candidate.Id == repairOrderId);
        if (repairOrder.Status == RepairOrderStatus.Draft)
        {
            repairOrder.StartWork();
        }

        if (repairOrder.Status == RepairOrderStatus.InProgress)
        {
            repairOrder.Complete(DefaultNow);
        }

        await context.SaveChangesAsync();
    }

    public sealed record BillingScenario(Guid OrganizationId, Guid ManagerId, Guid RepairOrderId);
}
