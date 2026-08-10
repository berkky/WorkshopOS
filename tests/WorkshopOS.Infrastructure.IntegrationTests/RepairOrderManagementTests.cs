using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.RepairOrders;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.RepairOrders;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class RepairOrderManagementServiceTests(PostgreSqlTestFixture fixture)
{
    private static readonly DateTimeOffset DefaultNow = new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DefaultAppointmentStart = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DefaultAppointmentEnd = new(2026, 9, 1, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RepairOrderManagement_Create_UsesCurrentOrganization()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var (location, customer, vehicle) = await PersistIntakePrerequisitesAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.CreateRepairOrderAsync(CreateWalkInCommand(location.Id, customer.Id, vehicle.Id));

        Assert.True(result.Success);
        var repairOrder = await writeScope.RepairOrders.SingleAsync(candidate => candidate.Id == result.Value);
        Assert.Equal(organization.Id, repairOrder.OrganizationId);
        Assert.Equal(location.Id, repairOrder.WorkshopLocationId);
        Assert.Equal(customer.Id, repairOrder.CustomerId);
        Assert.Equal(vehicle.Id, repairOrder.VehicleId);
        Assert.Equal(RepairOrderStatus.Draft, repairOrder.Status);
        Assert.Null(repairOrder.AppointmentId);
    }

    [Fact]
    public async Task RepairOrderManagement_Create_WhenOrganizationUnresolved_FailsClosed()
    {
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var service = CreateService(scope.Context, new UnresolvedOrganizationContext(), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateRepairOrderAsync(new CreateRepairOrderCommand
        {
            WorkshopLocationId = Guid.CreateVersion7(),
            CustomerId = Guid.CreateVersion7(),
            VehicleId = Guid.CreateVersion7(),
        });

        Assert.False(result.Success);
        Assert.Equal(RepairOrderOperationFailureReason.OrganizationUnresolved, result.FailureReason);
        Assert.Equal(0, await scope.Context.RepairOrders.CountAsync());
    }

    [Fact]
    public async Task RepairOrderManagement_Create_RejectsOtherTenantCustomer()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid customerBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            customerBId = (await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix)).Id;
        }

        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        var (locationA, _, vehicleA) = await PersistIntakePrerequisitesAsync(writeScopeA, organizationA.Id, suffix);
        var service = CreateService(writeScopeA, new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateRepairOrderAsync(CreateWalkInCommand(locationA.Id, customerBId, vehicleA.Id));

        Assert.False(result.Success);
        Assert.Equal(RepairOrderOperationFailureReason.CustomerNotFound, result.FailureReason);
    }

    [Fact]
    public async Task RepairOrderManagement_Create_RejectsOtherTenantVehicle()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid vehicleBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
            vehicleBId = (await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id)).Id;
        }

        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        var (locationA, customerA, _) = await PersistIntakePrerequisitesAsync(writeScopeA, organizationA.Id, suffix);
        var service = CreateService(writeScopeA, new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateRepairOrderAsync(CreateWalkInCommand(locationA.Id, customerA.Id, vehicleBId));

        Assert.False(result.Success);
        Assert.Equal(RepairOrderOperationFailureReason.VehicleNotFound, result.FailureReason);
    }

    [Fact]
    public async Task RepairOrderManagement_Create_RejectsOtherTenantLocation()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid locationBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            locationBId = (await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix)).Id;
        }

        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        var (_, customerA, vehicleA) = await PersistIntakePrerequisitesAsync(writeScopeA, organizationA.Id, suffix);
        var service = CreateService(writeScopeA, new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateRepairOrderAsync(CreateWalkInCommand(locationBId, customerA.Id, vehicleA.Id));

        Assert.False(result.Success);
        Assert.Equal(RepairOrderOperationFailureReason.WorkshopLocationNotFound, result.FailureReason);
    }

    [Fact]
    public async Task RepairOrderManagement_Create_RejectsCustomerVehicleMismatch()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var location = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, organization.Id, suffix);
        var customerA = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, $"{suffix}-a");
        var customerB = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, $"{suffix}-b");
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, organization.Id, suffix, customerB.Id);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.CreateRepairOrderAsync(CreateWalkInCommand(location.Id, customerA.Id, vehicle.Id));

        Assert.False(result.Success);
        Assert.Equal(RepairOrderOperationFailureReason.CustomerVehicleMismatch, result.FailureReason);
    }

    [Fact]
    public async Task RepairOrderManagement_WalkIn_CreatesWithoutAppointment()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var (location, customer, vehicle) = await PersistIntakePrerequisitesAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.CreateRepairOrderAsync(CreateWalkInCommand(location.Id, customer.Id, vehicle.Id));

        Assert.True(result.Success);
        var repairOrder = await writeScope.RepairOrders.SingleAsync(candidate => candidate.Id == result.Value);
        Assert.Null(repairOrder.AppointmentId);
    }

    [Fact]
    public async Task RepairOrderManagement_CreateFromAppointment_UsesAppointmentRelationships()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var (location, customer, vehicle) = await PersistIntakePrerequisitesAsync(writeScope, organization.Id, suffix);
        var appointment = new Domain.Appointments.Appointment(
            organization.Id,
            location.Id,
            customer.Id,
            vehicle.Id,
            DefaultAppointmentStart,
            DefaultAppointmentEnd,
            customerConcern: "Appointment concern",
            internalNotes: "Appointment notes");
        writeScope.Appointments.Add(appointment);
        await writeScope.SaveChangesAsync();

        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));
        var result = await service.CreateRepairOrderFromAppointmentAsync(new CreateRepairOrderFromAppointmentCommand
        {
            AppointmentId = appointment.Id,
        });

        Assert.True(result.Success);
        var repairOrder = await writeScope.RepairOrders.SingleAsync(candidate => candidate.Id == result.Value);
        Assert.Equal(appointment.Id, repairOrder.AppointmentId);
        Assert.Equal(location.Id, repairOrder.WorkshopLocationId);
        Assert.Equal(customer.Id, repairOrder.CustomerId);
        Assert.Equal(vehicle.Id, repairOrder.VehicleId);
        Assert.Equal("Appointment concern", repairOrder.CustomerConcern);
        Assert.Equal("Appointment notes", repairOrder.InternalNotes);
    }

    [Fact]
    public async Task RepairOrderManagement_CreateFromAppointment_RejectsOtherTenantAppointment()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid appointmentBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
            var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
            var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id);
            appointmentBId = (await TestDataFactory.PersistAppointmentAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                customerB.Id,
                vehicleB.Id,
                DefaultAppointmentStart,
                DefaultAppointmentEnd)).Id;
        }

        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        var service = CreateService(writeScopeA, new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateRepairOrderFromAppointmentAsync(new CreateRepairOrderFromAppointmentCommand
        {
            AppointmentId = appointmentBId,
        });

        Assert.False(result.Success);
        Assert.Equal(RepairOrderOperationFailureReason.AppointmentNotFound, result.FailureReason);
    }

    [Fact]
    public async Task RepairOrderManagement_CreateFromAppointment_RejectsDuplicateRepairOrder()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var (location, customer, vehicle) = await PersistIntakePrerequisitesAsync(writeScope, organization.Id, suffix);
        var appointment = await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            organization.Id,
            location.Id,
            customer.Id,
            vehicle.Id,
            DefaultAppointmentStart,
            DefaultAppointmentEnd);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var first = await service.CreateRepairOrderFromAppointmentAsync(new CreateRepairOrderFromAppointmentCommand
        {
            AppointmentId = appointment.Id,
        });
        Assert.True(first.Success);

        var second = await service.CreateRepairOrderFromAppointmentAsync(new CreateRepairOrderFromAppointmentCommand
        {
            AppointmentId = appointment.Id,
        });

        Assert.False(second.Success);
        Assert.Equal(RepairOrderOperationFailureReason.DuplicateAppointmentRepairOrder, second.FailureReason);
        Assert.Equal(1, await writeScope.RepairOrders.CountAsync(candidate => candidate.AppointmentId == appointment.Id));
    }

    [Fact]
    public async Task RepairOrderManagement_List_ReturnsOnlyCurrentOrganizationRepairOrders()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using (var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow)))
        {
            var (locationA, customerA, vehicleA) = await PersistIntakePrerequisitesAsync(scopeA, organizationA.Id, suffix);
            await TestDataFactory.PersistRepairOrderAsync(
                scopeA,
                organizationA.Id,
                locationA.Id,
                customerA.Id,
                vehicleA.Id,
                $"{suffix}-a",
                DefaultNow);
        }

        Guid repairOrderBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow)))
        {
            var (locationB, customerB, vehicleB) = await PersistIntakePrerequisitesAsync(scopeB, organizationB.Id, suffix);
            repairOrderBId = (await TestDataFactory.PersistRepairOrderAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                customerB.Id,
                vehicleB.Id,
                $"{suffix}-b",
                DefaultNow)).Id;
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = CreateService(readScopeA, new TestOrganizationContext(organizationA.Id));
        var result = await service.ListRepairOrdersAsync(new RepairOrderListQuery());

        Assert.Equal(1, result.TotalCount);
        Assert.DoesNotContain(result.Items, item => item.RepairOrderId == repairOrderBId);
    }

    [Fact]
    public async Task RepairOrderManagement_Details_DoesNotExposeOtherTenantRepairOrder()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid repairOrderBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow)))
        {
            var (locationB, customerB, vehicleB) = await PersistIntakePrerequisitesAsync(scopeB, organizationB.Id, suffix);
            repairOrderBId = (await TestDataFactory.PersistRepairOrderAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                customerB.Id,
                vehicleB.Id,
                suffix,
                DefaultNow)).Id;
        }

        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = CreateService(readScopeA, new TestOrganizationContext(organizationA.Id));

        var details = await service.GetRepairOrderDetailsAsync(repairOrderBId);
        Assert.Null(details);
    }

    [Fact]
    public async Task RepairOrderManagement_Update_DoesNotModifyOtherTenantRepairOrder()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid repairOrderBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow)))
        {
            var (locationB, customerB, vehicleB) = await PersistIntakePrerequisitesAsync(scopeB, organizationB.Id, suffix);
            repairOrderBId = (await TestDataFactory.PersistRepairOrderAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                customerB.Id,
                vehicleB.Id,
                suffix,
                DefaultNow)).Id;
        }

        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = CreateService(writeScopeA, new TestOrganizationContext(organizationA.Id));

        var result = await service.UpdateRepairOrderIntakeAsync(new UpdateRepairOrderIntakeCommand
        {
            RepairOrderId = repairOrderBId,
            CustomerConcern = "Should not apply",
        });

        Assert.False(result.Success);
        Assert.Equal(RepairOrderOperationFailureReason.RepairOrderNotFound, result.FailureReason);
    }

    [Fact]
    public async Task RepairOrderManagement_Update_DoesNotChangeHistoricalCustomerOrVehicle()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var (location, customer, vehicle) = await PersistIntakePrerequisitesAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var createResult = await service.CreateRepairOrderAsync(CreateWalkInCommand(location.Id, customer.Id, vehicle.Id));
        Assert.True(createResult.Success);

        var updateResult = await service.UpdateRepairOrderIntakeAsync(new UpdateRepairOrderIntakeCommand
        {
            RepairOrderId = createResult.Value,
            CustomerConcern = "Updated concern",
            InternalNotes = "Updated notes",
            Odometer = 42_000,
        });
        Assert.True(updateResult.Success);

        var repairOrder = await writeScope.RepairOrders.SingleAsync(candidate => candidate.Id == createResult.Value);
        Assert.Equal(customer.Id, repairOrder.CustomerId);
        Assert.Equal(vehicle.Id, repairOrder.VehicleId);
        Assert.Equal("Updated concern", repairOrder.CustomerConcern);
        Assert.Equal("Updated notes", repairOrder.InternalNotes);
        Assert.Equal(42_000, repairOrder.Odometer);
    }

    [Fact]
    public async Task RepairOrderNumber_IsServerControlled()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var (location, customer, vehicle) = await PersistIntakePrerequisitesAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.CreateRepairOrderAsync(CreateWalkInCommand(location.Id, customer.Id, vehicle.Id));
        Assert.True(result.Success);

        var repairOrder = await writeScope.RepairOrders.SingleAsync(candidate => candidate.Id == result.Value);
        Assert.Matches(@"^RO-\d{8}-[A-F0-9]{8}$", repairOrder.Number);
    }

    [Fact]
    public async Task RepairOrderNumber_IsOrganizationUnique()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var (location, customer, vehicle) = await PersistIntakePrerequisitesAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var first = await service.CreateRepairOrderAsync(CreateWalkInCommand(location.Id, customer.Id, vehicle.Id));
        var second = await service.CreateRepairOrderAsync(CreateWalkInCommand(location.Id, customer.Id, vehicle.Id));
        Assert.True(first.Success);
        Assert.True(second.Success);

        var numbers = await writeScope.RepairOrders
            .Where(candidate => candidate.OrganizationId == organization.Id)
            .Select(candidate => candidate.Number)
            .ToListAsync();
        Assert.Equal(2, numbers.Count);
        Assert.NotEqual(numbers[0], numbers[1]);
    }

    [Fact]
    public async Task RepairOrderLifecycle_AllowsValidTransition()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var timeProvider = new FakeTimeProvider(DefaultNow);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext, timeProvider);
        var (location, customer, vehicle) = await PersistIntakePrerequisitesAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext, timeProvider);

        var createResult = await service.CreateRepairOrderAsync(CreateWalkInCommand(location.Id, customer.Id, vehicle.Id));
        Assert.True(createResult.Success);

        Assert.True((await service.StartRepairOrderAsync(createResult.Value)).Success);
        Assert.True((await service.CompleteRepairOrderAsync(createResult.Value)).Success);

        var repairOrder = await writeScope.RepairOrders.SingleAsync(candidate => candidate.Id == createResult.Value);
        Assert.Equal(RepairOrderStatus.Completed, repairOrder.Status);
        Assert.Equal(DefaultNow, repairOrder.CompletedAtUtc);
    }

    [Fact]
    public async Task RepairOrderLifecycle_RejectsInvalidTransition()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var (location, customer, vehicle) = await PersistIntakePrerequisitesAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var createResult = await service.CreateRepairOrderAsync(CreateWalkInCommand(location.Id, customer.Id, vehicle.Id));
        Assert.True(createResult.Success);

        var completeResult = await service.CompleteRepairOrderAsync(createResult.Value);
        Assert.False(completeResult.Success);
        Assert.Equal(RepairOrderOperationFailureReason.InvalidLifecycleTransition, completeResult.FailureReason);
    }

    [Fact]
    public async Task RepairOrderLifecycle_TerminalStateCannotReturnToActive()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var timeProvider = new FakeTimeProvider(DefaultNow);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext, timeProvider);
        var (location, customer, vehicle) = await PersistIntakePrerequisitesAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext, timeProvider);

        var createResult = await service.CreateRepairOrderAsync(CreateWalkInCommand(location.Id, customer.Id, vehicle.Id));
        Assert.True(createResult.Success);
        Assert.True((await service.CancelRepairOrderAsync(createResult.Value)).Success);

        var startResult = await service.StartRepairOrderAsync(createResult.Value);
        Assert.False(startResult.Success);
        Assert.Equal(RepairOrderOperationFailureReason.InvalidLifecycleTransition, startResult.FailureReason);
    }

    private static RepairOrderManagementService CreateService(
        AppDbContext context,
        IOrganizationContext organizationContext,
        TimeProvider? timeProvider = null) =>
        new(
            context,
            organizationContext,
            new RepairOrderNumberGenerator(),
            timeProvider ?? TimeProvider.System);

    private static async Task<(Domain.Organizations.WorkshopLocation Location, Domain.Customers.Customer Customer, Domain.Vehicles.Vehicle Vehicle)>
        PersistIntakePrerequisitesAsync(AppDbContext context, Guid organizationId, string suffix)
    {
        var location = await TestDataFactory.PersistWorkshopLocationAsync(context, organizationId, suffix);
        var customer = await TestDataFactory.PersistCustomerAsync(context, organizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(context, organizationId, suffix, customer.Id);
        return (location, customer, vehicle);
    }

    private static CreateRepairOrderCommand CreateWalkInCommand(
        Guid workshopLocationId,
        Guid customerId,
        Guid vehicleId) =>
        new()
        {
            WorkshopLocationId = workshopLocationId,
            CustomerId = customerId,
            VehicleId = vehicleId,
        };
}

[Collection(PostgreSqlCollection.Name)]
public sealed class RepairOrderManagerAuthorizationTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task RepairOrderManager_AllowsOwner()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Owner, shouldSucceed: true);
    }

    [Fact]
    public async Task RepairOrderManager_AllowsAdministrator()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Administrator, shouldSucceed: true);
    }

    [Fact]
    public async Task RepairOrderManager_AllowsServiceAdvisor()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.ServiceAdvisor, shouldSucceed: true);
    }

    [Fact]
    public async Task RepairOrderManager_RejectsTechnician()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Technician, shouldSucceed: false);
    }

    [Fact]
    public async Task RepairOrderManager_RejectsViewer()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Viewer, shouldSucceed: false);
    }

    [Fact]
    public async Task RepairOrderManager_ReflectsDatabaseRoleChange()
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
        var handler = new RepairOrderManagerAuthorizationHandler(writeScope, organizationContext);

        var initialContext = new AuthorizationHandlerContext(
            [new RepairOrderManagerRequirement()],
            CreatePrincipal(advisor.Id),
            resource: null);
        await handler.HandleAsync(initialContext);
        Assert.True(initialContext.HasSucceeded);

        var advisorMembership = await writeScope.OrganizationMemberships
            .SingleAsync(membership => membership.UserId == advisor.Id);
        advisorMembership.ChangeRole(OrganizationMembershipRole.Technician);
        await writeScope.SaveChangesAsync();

        var afterContext = new AuthorizationHandlerContext(
            [new RepairOrderManagerRequirement()],
            CreatePrincipal(advisor.Id),
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
        var handler = new RepairOrderManagerAuthorizationHandler(writeScope, organizationContext);

        var context = new AuthorizationHandlerContext(
            [new RepairOrderManagerRequirement()],
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
