using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Appointments;
using WorkshopOS.Application.Billing;
using WorkshopOS.Application.CustomerPortal;
using WorkshopOS.Application.Estimates;
using WorkshopOS.Application.Inspections;
using WorkshopOS.Application.Operations;
using WorkshopOS.Domain.Appointments;
using WorkshopOS.Domain.Billing;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Infrastructure.Appointments;
using WorkshopOS.Infrastructure.Inspections;
using WorkshopOS.Infrastructure.Operations;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class EndToEndCommercialLifecycleTests(PostgreSqlTestFixture fixture)
{
    private static readonly DateTimeOffset DefaultNow = new(2026, 8, 10, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EndToEnd_WorkshopCommercialLifecycle_CompletesSuccessfully()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator(manager.OrganizationId);
        var organizationContext = new TestOrganizationContext(manager.OrganizationId);

        await using var writeScope = scope.CreateContext(organizationContext, clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(
            writeScope,
            manager.OrganizationId,
            suffix,
            RepairOrderStatus.Draft);
        var repairOrderId = scenario.RepairOrder.Id;
        var originalCustomerId = scenario.RepairOrder.CustomerId;

        var appointmentService = new AppointmentManagementService(writeScope, organizationContext, clock);
        var appointmentResult = await appointmentService.CreateAppointmentAsync(new CreateAppointmentCommand
        {
            WorkshopLocationId = scenario.Location.Id,
            CustomerId = scenario.Customer.Id,
            VehicleId = scenario.Vehicle.Id,
            ScheduledStartUtc = DefaultNow.AddHours(1),
            ScheduledEndUtc = DefaultNow.AddHours(2),
        });
        Assert.True(appointmentResult.Success);
        var appointmentId = appointmentResult.Value!;
        var appointmentStatusBefore = (await writeScope.Appointments.SingleAsync(a => a.Id == appointmentId)).Status;

        var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-tech");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            manager.OrganizationId,
            technicianUser.Id,
            OrganizationMembershipRole.Technician);
        var technician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            manager.OrganizationId,
            scenario.Location.Id,
            suffix,
            technicianUser.Id);

        var operationsService = new WorkshopOperationsService(writeScope, organizationContext, clock);
        Assert.True((await operationsService.AssignTechnicianAsync(
            manager.ManagerId,
            new AssignTechnicianCommand
            {
                RepairOrderId = repairOrderId,
                StaffMemberId = technician.StaffMember.Id,
            })).Success);

        var inspectionService = new InspectionManagementService(writeScope, organizationContext, clock);
        var inspectionId = (await inspectionService.CreateInspectionAsync(manager.ManagerId, repairOrderId)).Value!;
        await inspectionService.StartInspectionAsync(manager.ManagerId, inspectionId);
        var inspectionItems = await writeScope.InspectionItems
            .Where(candidate => candidate.InspectionId == inspectionId)
            .ToListAsync();
        await inspectionService.UpdateInspectionItemsAsync(
            manager.ManagerId,
            new UpdateInspectionItemsCommand
            {
                InspectionId = inspectionId,
                Items = inspectionItems.Select(item => new InspectionItemUpdate
                {
                    InspectionItemId = item.Id,
                    Condition = InspectionCondition.Good,
                }).ToList(),
            });
        Assert.True((await inspectionService.CompleteInspectionAsync(manager.ManagerId, inspectionId)).Success);

        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, organizationContext, clock);
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(
            estimateService,
            manager.ManagerId,
            repairOrderId,
            "Brake service line",
            250m);
        var share = await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            organizationContext,
            clock,
            manager.ManagerId,
            estimateId);

        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);
        Assert.True((await portalService.ExchangeTokenAsync(new ExchangeEstimateShareTokenCommand
        {
            PublicId = share.PublicId,
            RawToken = share.RawToken,
        })).Success);
        Assert.True((await portalService.RecordApprovalAsync(share.PublicId)).Success);

        var inventoryMovementsBefore = await writeScope.PartInventoryMovements.CountAsync();

        var invoiceService = BillingTestSupport.CreateInvoiceService(writeScope, organizationContext, clock);
        var paymentService = BillingTestSupport.CreatePaymentService(writeScope, organizationContext, clock);
        var invoiceId = (await invoiceService.CreateInvoiceFromApprovedEstimateAsync(manager.ManagerId, estimateId)).Value!;
        Assert.True((await invoiceService.IssueInvoiceAsync(manager.ManagerId, invoiceId)).Success);

        var invoiceDetails = await invoiceService.GetInvoiceDetailsAsync(invoiceId);
        Assert.NotNull(invoiceDetails);
        Assert.True((await paymentService.RecordPaymentAsync(
            manager.ManagerId,
            new RecordPaymentCommand
            {
                InvoiceId = invoiceId,
                Amount = invoiceDetails.Total,
                PaymentMethod = PaymentMethod.Cash,
            })).Success);
        var paidDetails = await invoiceService.GetInvoiceDetailsAsync(invoiceId);
        Assert.NotNull(paidDetails);

        await BillingTestSupport.CompleteRepairOrderAsync(writeScope, repairOrderId);
        Assert.True((await paymentService.CloseRepairOrderCommerciallyAsync(manager.ManagerId, repairOrderId)).Success);

        var repairOrder = await writeScope.RepairOrders.SingleAsync(candidate => candidate.Id == repairOrderId);
        var estimate = await writeScope.Estimates.SingleAsync(candidate => candidate.Id == estimateId);
        var estimateShare = await writeScope.EstimateShares.SingleAsync(candidate => candidate.EstimateId == estimateId);
        var invoice = await writeScope.Invoices.SingleAsync(candidate => candidate.Id == invoiceId);
        var inspection = await writeScope.Inspections.SingleAsync(candidate => candidate.Id == inspectionId);
        var appointment = await writeScope.Appointments.SingleAsync(candidate => candidate.Id == appointmentId);
        var assignment = await writeScope.RepairOrderTechnicianAssignments.SingleAsync(
            candidate => candidate.RepairOrderId == repairOrderId);

        Assert.Equal(originalCustomerId, repairOrder.CustomerId);
        Assert.Equal(technician.StaffMember.Id, assignment.StaffMemberId);
        Assert.Equal(InspectionStatus.Completed, inspection.Status);
        Assert.Equal(EstimateStatus.Approved, estimate.Status);
        Assert.Equal(EstimateShareDecision.Approved, estimateShare.Decision);
        Assert.NotNull(estimateShare.DecisionAtUtc);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Equal(InvoicePaymentState.Paid, paidDetails.PaymentState);
        Assert.Equal(RepairOrderStatus.Completed, repairOrder.Status);
        Assert.NotNull(repairOrder.CommerciallyClosedAtUtc);
        Assert.Equal(inventoryMovementsBefore, await writeScope.PartInventoryMovements.CountAsync());
        Assert.Equal(appointmentStatusBefore, appointment.Status);
    }

    [Fact]
    public async Task EndToEnd_StaffRecordedApproval_CommercialLifecycleCompletes()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await BillingTestSupport.CreateBillingScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);
        var clock = new FakeTimeProvider(BillingTestSupport.DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, clock);
        var estimateService = BillingTestSupport.CreateEstimateService(writeScope, organizationContext, clock);
        var invoiceService = BillingTestSupport.CreateInvoiceService(writeScope, organizationContext, clock);
        var paymentService = BillingTestSupport.CreatePaymentService(writeScope, organizationContext, clock);

        var estimateId = await BillingTestSupport.CreateApprovedEstimateAsync(
            estimateService,
            scenario.ManagerId,
            scenario.RepairOrderId);
        var invoiceId = (await invoiceService.CreateInvoiceFromApprovedEstimateAsync(scenario.ManagerId, estimateId)).Value!;
        Assert.True((await invoiceService.IssueInvoiceAsync(scenario.ManagerId, invoiceId)).Success);

        var details = await invoiceService.GetInvoiceDetailsAsync(invoiceId);
        Assert.NotNull(details);
        Assert.True((await paymentService.RecordPaymentAsync(
            scenario.ManagerId,
            new RecordPaymentCommand
            {
                InvoiceId = invoiceId,
                Amount = details.Total,
                PaymentMethod = PaymentMethod.BankTransfer,
            })).Success);

        await BillingTestSupport.CompleteRepairOrderAsync(writeScope, scenario.RepairOrderId);
        Assert.True((await paymentService.CloseRepairOrderCommerciallyAsync(scenario.ManagerId, scenario.RepairOrderId)).Success);

        var estimate = await writeScope.Estimates.SingleAsync(candidate => candidate.Id == estimateId);
        var repairOrder = await writeScope.RepairOrders.SingleAsync(candidate => candidate.Id == scenario.RepairOrderId);
        Assert.Equal(EstimateStatus.Approved, estimate.Status);
        Assert.Equal(RepairOrderStatus.Completed, repairOrder.Status);
        Assert.NotNull(repairOrder.CommerciallyClosedAtUtc);
        Assert.Equal(0, await writeScope.EstimateShares.CountAsync());
    }

    [Fact]
    public async Task EndToEnd_CrossTenantInvoiceAccess_IsDenied()
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
            invoiceBId = await BillingTestSupport.CreateIssuedInvoiceAsync(invoiceService, managerB.Id, repairOrder.Id);
        }

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(BillingTestSupport.DefaultNow));
        var invoiceServiceA = BillingTestSupport.CreateInvoiceService(scopeA, new TestOrganizationContext(organizationA.Id));
        var paymentServiceA = BillingTestSupport.CreatePaymentService(scopeA, new TestOrganizationContext(organizationA.Id));

        var details = await invoiceServiceA.GetInvoiceDetailsAsync(invoiceBId);
        var payment = await paymentServiceA.RecordPaymentAsync(
            managerA.Id,
            new RecordPaymentCommand
            {
                InvoiceId = invoiceBId,
                Amount = 50m,
                PaymentMethod = PaymentMethod.Cash,
            });

        Assert.Null(details);
        Assert.False(payment.Success);
        Assert.Equal(0, await scopeA.Invoices.CountAsync());
    }
}
