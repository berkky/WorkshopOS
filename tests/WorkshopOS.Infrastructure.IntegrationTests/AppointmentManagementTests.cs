using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Appointments;
using WorkshopOS.Domain.Appointments;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Appointments;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class AppointmentManagementServiceTests(PostgreSqlTestFixture fixture)
{
    private static readonly DateTimeOffset DefaultNow = new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DefaultStart = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DefaultEnd = new(2026, 9, 1, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AppointmentManagement_Create_UsesCurrentOrganization()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var (location, customer, vehicle) = await PersistBookingPrerequisitesAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.CreateAppointmentAsync(CreateCommand(location.Id, customer.Id, vehicle.Id));

        Assert.True(result.Success);
        var appointment = await writeScope.Appointments.SingleAsync(candidate => candidate.Id == result.Value);
        Assert.Equal(organization.Id, appointment.OrganizationId);
        Assert.Equal(location.Id, appointment.WorkshopLocationId);
        Assert.Equal(customer.Id, appointment.CustomerId);
        Assert.Equal(vehicle.Id, appointment.VehicleId);
        Assert.Equal(DefaultStart, appointment.ScheduledStartUtc);
        Assert.Equal(DefaultEnd, appointment.ScheduledEndUtc);
    }

    [Fact]
    public async Task AppointmentManagement_Create_WhenOrganizationUnresolved_FailsClosed()
    {
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var service = CreateService(scope.Context, new UnresolvedOrganizationContext(), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateAppointmentAsync(new CreateAppointmentCommand
        {
            WorkshopLocationId = Guid.CreateVersion7(),
            CustomerId = Guid.CreateVersion7(),
            VehicleId = Guid.CreateVersion7(),
            ScheduledStartUtc = DefaultStart,
            ScheduledEndUtc = DefaultEnd,
        });

        Assert.False(result.Success);
        Assert.Equal(AppointmentOperationFailureReason.OrganizationUnresolved, result.FailureReason);
        Assert.Equal(0, await scope.Context.Appointments.CountAsync());
    }

    [Fact]
    public async Task AppointmentManagement_Create_RejectsOtherTenantCustomer()
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
        var (locationA, customerA, vehicleA) = await PersistBookingPrerequisitesAsync(writeScopeA, organizationA.Id, suffix);
        var service = CreateService(writeScopeA, new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateAppointmentAsync(CreateCommand(locationA.Id, customerBId, vehicleA.Id));

        Assert.False(result.Success);
        Assert.Equal(AppointmentOperationFailureReason.CustomerNotFound, result.FailureReason);
    }

    [Fact]
    public async Task AppointmentManagement_Create_RejectsOtherTenantVehicle()
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
        var (locationA, customerA, _) = await PersistBookingPrerequisitesAsync(writeScopeA, organizationA.Id, suffix);
        var service = CreateService(writeScopeA, new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateAppointmentAsync(CreateCommand(locationA.Id, customerA.Id, vehicleBId));

        Assert.False(result.Success);
        Assert.Equal(AppointmentOperationFailureReason.VehicleNotFound, result.FailureReason);
    }

    [Fact]
    public async Task AppointmentManagement_Create_RejectsOtherTenantLocation()
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
        var (_, customerA, vehicleA) = await PersistBookingPrerequisitesAsync(writeScopeA, organizationA.Id, suffix);
        var service = CreateService(writeScopeA, new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateAppointmentAsync(CreateCommand(locationBId, customerA.Id, vehicleA.Id));

        Assert.False(result.Success);
        Assert.Equal(AppointmentOperationFailureReason.WorkshopLocationNotFound, result.FailureReason);
    }

    [Fact]
    public async Task AppointmentManagement_List_ReturnsOnlyCurrentOrganizationAppointments()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using (var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id)))
        {
            var (locationA, customerA, vehicleA) = await PersistBookingPrerequisitesAsync(scopeA, organizationA.Id, $"{suffix}-a");
            await TestDataFactory.PersistAppointmentAsync(
                scopeA,
                organizationA.Id,
                locationA.Id,
                customerA.Id,
                vehicleA.Id,
                DefaultStart,
                DefaultEnd);
        }

        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var (locationB, customerB, vehicleB) = await PersistBookingPrerequisitesAsync(scopeB, organizationB.Id, $"{suffix}-b");
            await TestDataFactory.PersistAppointmentAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                customerB.Id,
                vehicleB.Id,
                DefaultStart.AddHours(2),
                DefaultEnd.AddHours(2));
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = CreateService(readScopeA, new TestOrganizationContext(organizationA.Id));
        var result = await service.ListAppointmentsAsync(new AppointmentListQuery());

        Assert.Single(result.Items);
        Assert.Equal(DefaultStart, result.Items[0].ScheduledStartUtc);
    }

    [Fact]
    public async Task AppointmentManagement_Details_DoesNotExposeOtherTenantAppointment()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid appointmentBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var (locationB, customerB, vehicleB) = await PersistBookingPrerequisitesAsync(scopeB, organizationB.Id, suffix);
            appointmentBId = (await TestDataFactory.PersistAppointmentAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                customerB.Id,
                vehicleB.Id,
                DefaultStart,
                DefaultEnd)).Id;
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = CreateService(readScopeA, new TestOrganizationContext(organizationA.Id));
        var details = await service.GetAppointmentDetailsAsync(appointmentBId);

        Assert.Null(details);
    }

    [Fact]
    public async Task AppointmentManagement_Update_DoesNotModifyOtherTenantAppointment()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid appointmentBId;
        Guid locationBId;
        Guid customerBId;
        Guid vehicleBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var (locationB, customerB, vehicleB) = await PersistBookingPrerequisitesAsync(scopeB, organizationB.Id, suffix);
            locationBId = locationB.Id;
            customerBId = customerB.Id;
            vehicleBId = vehicleB.Id;
            appointmentBId = (await TestDataFactory.PersistAppointmentAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                customerB.Id,
                vehicleB.Id,
                DefaultStart,
                DefaultEnd)).Id;
        }

        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = CreateService(writeScopeA, new TestOrganizationContext(organizationA.Id));

        var result = await service.UpdateAppointmentAsync(new UpdateAppointmentCommand
        {
            AppointmentId = appointmentBId,
            WorkshopLocationId = locationBId,
            CustomerId = customerBId,
            VehicleId = vehicleBId,
            CustomerConcern = "Cross tenant update",
        });

        Assert.False(result.Success);
        Assert.Equal(AppointmentOperationFailureReason.AppointmentNotFound, result.FailureReason);
    }

    [Fact]
    public async Task AppointmentManagement_Create_RejectsInvalidTimeRange()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var (location, customer, vehicle) = await PersistBookingPrerequisitesAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.CreateAppointmentAsync(CreateCommand(
            location.Id,
            customer.Id,
            vehicle.Id,
            DefaultStart,
            DefaultStart));

        Assert.False(result.Success);
        Assert.Equal(AppointmentOperationFailureReason.InvalidInput, result.FailureReason);
    }

    [Fact]
    public async Task AppointmentManagement_Create_RejectsPastStart()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var pastStart = new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);
        var pastEnd = pastStart.AddHours(1);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);
        var timeProvider = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, timeProvider);
        var (location, customer, vehicle) = await PersistBookingPrerequisitesAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext, timeProvider);

        var result = await service.CreateAppointmentAsync(CreateCommand(
            location.Id,
            customer.Id,
            vehicle.Id,
            pastStart,
            pastEnd));

        Assert.False(result.Success);
        Assert.Equal(AppointmentOperationFailureReason.PastStartNotAllowed, result.FailureReason);
    }

    [Fact]
    public async Task AppointmentManagement_Create_RejectsOverlappingVehicleAppointment()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var overlappingStart = DefaultStart.AddMinutes(30);
        var overlappingEnd = DefaultEnd.AddMinutes(30);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);
        var timeProvider = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, timeProvider);
        var (location, customer, vehicle) = await PersistBookingPrerequisitesAsync(writeScope, organization.Id, suffix);
        await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            organization.Id,
            location.Id,
            customer.Id,
            vehicle.Id,
            DefaultStart,
            DefaultEnd);
        var service = CreateService(writeScope, organizationContext, timeProvider);

        var result = await service.CreateAppointmentAsync(CreateCommand(
            location.Id,
            customer.Id,
            vehicle.Id,
            overlappingStart,
            overlappingEnd));

        Assert.False(result.Success);
        Assert.Equal(AppointmentOperationFailureReason.VehicleOverlap, result.FailureReason);
    }

    [Fact]
    public async Task AppointmentManagement_Create_AllowsAdjacentVehicleAppointment()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var adjacentStart = DefaultEnd;
        var adjacentEnd = DefaultEnd.AddHours(1);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);
        var timeProvider = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, timeProvider);
        var (location, customer, vehicle) = await PersistBookingPrerequisitesAsync(writeScope, organization.Id, suffix);
        await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            organization.Id,
            location.Id,
            customer.Id,
            vehicle.Id,
            DefaultStart,
            DefaultEnd);
        var service = CreateService(writeScope, organizationContext, timeProvider);

        var result = await service.CreateAppointmentAsync(CreateCommand(
            location.Id,
            customer.Id,
            vehicle.Id,
            adjacentStart,
            adjacentEnd));

        Assert.True(result.Success);
        Assert.Equal(2, await writeScope.Appointments.CountAsync());
    }

    [Fact]
    public async Task AppointmentManagement_Create_AllowsSameTimeForDifferentVehicles()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);
        var timeProvider = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, timeProvider);
        var location = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, organization.Id, suffix);
        var customerA = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, $"{suffix}-a");
        var customerB = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, $"{suffix}-b");
        var vehicleA = await TestDataFactory.PersistVehicleAsync(writeScope, organization.Id, $"{suffix}-a", customerA.Id);
        var vehicleB = await TestDataFactory.PersistVehicleAsync(writeScope, organization.Id, $"{suffix}-b", customerB.Id);
        await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            organization.Id,
            location.Id,
            customerA.Id,
            vehicleA.Id,
            DefaultStart,
            DefaultEnd);
        var service = CreateService(writeScope, organizationContext, timeProvider);

        var result = await service.CreateAppointmentAsync(CreateCommand(
            location.Id,
            customerB.Id,
            vehicleB.Id,
            DefaultStart,
            DefaultEnd));

        Assert.True(result.Success);
        Assert.Equal(2, await writeScope.Appointments.CountAsync());
    }

    [Fact]
    public async Task AppointmentManagement_Cancel_PreservesHistoricalRecord()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var (location, customer, vehicle) = await PersistBookingPrerequisitesAsync(writeScope, organization.Id, suffix);
        var appointment = await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            organization.Id,
            location.Id,
            customer.Id,
            vehicle.Id,
            DefaultStart,
            DefaultEnd);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.CancelAppointmentAsync(appointment.Id);

        Assert.True(result.Success);
        Assert.Equal(1, await writeScope.Appointments.CountAsync());

        var cancelled = await writeScope.Appointments.SingleAsync(candidate => candidate.Id == appointment.Id);
        Assert.Equal(AppointmentStatus.Cancelled, cancelled.Status);

        var details = await service.GetAppointmentDetailsAsync(appointment.Id);
        Assert.NotNull(details);
        Assert.Equal(AppointmentStatus.Cancelled, details.Status);
    }

    [Fact]
    public async Task AppointmentManagement_CancelledAppointment_NoLongerBlocksVehicleTime()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);
        var timeProvider = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, timeProvider);
        var (location, customer, vehicle) = await PersistBookingPrerequisitesAsync(writeScope, organization.Id, suffix);
        var appointment = await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            organization.Id,
            location.Id,
            customer.Id,
            vehicle.Id,
            DefaultStart,
            DefaultEnd);
        var service = CreateService(writeScope, organizationContext, timeProvider);

        Assert.True((await service.CancelAppointmentAsync(appointment.Id)).Success);

        var result = await service.CreateAppointmentAsync(CreateCommand(
            location.Id,
            customer.Id,
            vehicle.Id,
            DefaultStart,
            DefaultEnd));

        Assert.True(result.Success);
        Assert.Equal(2, await writeScope.Appointments.CountAsync());
    }

    [Fact]
    public async Task AppointmentManagement_Reschedule_RejectsOverlap()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var secondStart = DefaultEnd.AddHours(1);
        var secondEnd = secondStart.AddHours(1);
        var conflictingStart = DefaultStart.AddMinutes(30);
        var conflictingEnd = DefaultEnd.AddMinutes(30);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);
        var timeProvider = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, timeProvider);
        var (location, customer, vehicle) = await PersistBookingPrerequisitesAsync(writeScope, organization.Id, suffix);
        await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            organization.Id,
            location.Id,
            customer.Id,
            vehicle.Id,
            DefaultStart,
            DefaultEnd);
        var secondAppointment = await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            organization.Id,
            location.Id,
            customer.Id,
            vehicle.Id,
            secondStart,
            secondEnd);
        var service = CreateService(writeScope, organizationContext, timeProvider);

        var result = await service.RescheduleAppointmentAsync(new RescheduleAppointmentCommand
        {
            AppointmentId = secondAppointment.Id,
            WorkshopLocationId = location.Id,
            ScheduledStartUtc = conflictingStart,
            ScheduledEndUtc = conflictingEnd,
        });

        Assert.False(result.Success);
        Assert.Equal(AppointmentOperationFailureReason.VehicleOverlap, result.FailureReason);
    }

    [Fact]
    public async Task AppointmentManagement_Reschedule_ExcludesCurrentAppointment()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var extendedStart = DefaultStart.AddMinutes(-30);
        var extendedEnd = DefaultEnd.AddHours(1);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);
        var timeProvider = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, timeProvider);
        var (location, customer, vehicle) = await PersistBookingPrerequisitesAsync(writeScope, organization.Id, suffix);
        var appointment = await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            organization.Id,
            location.Id,
            customer.Id,
            vehicle.Id,
            DefaultStart,
            DefaultEnd);
        var service = CreateService(writeScope, organizationContext, timeProvider);

        var result = await service.RescheduleAppointmentAsync(new RescheduleAppointmentCommand
        {
            AppointmentId = appointment.Id,
            WorkshopLocationId = location.Id,
            ScheduledStartUtc = extendedStart,
            ScheduledEndUtc = extendedEnd,
        });

        Assert.True(result.Success);

        var rescheduled = await writeScope.Appointments.SingleAsync(candidate => candidate.Id == appointment.Id);
        Assert.Equal(extendedStart, rescheduled.ScheduledStartUtc);
        Assert.Equal(extendedEnd, rescheduled.ScheduledEndUtc);
    }

    private static AppointmentManagementService CreateService(
        AppDbContext context,
        IOrganizationContext organizationContext,
        TimeProvider? timeProvider = null) =>
        new(context, organizationContext, timeProvider ?? TimeProvider.System);

    private static async Task<(Domain.Organizations.WorkshopLocation Location, Domain.Customers.Customer Customer, Domain.Vehicles.Vehicle Vehicle)>
        PersistBookingPrerequisitesAsync(AppDbContext context, Guid organizationId, string suffix)
    {
        var location = await TestDataFactory.PersistWorkshopLocationAsync(context, organizationId, suffix);
        var customer = await TestDataFactory.PersistCustomerAsync(context, organizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(context, organizationId, suffix, customer.Id);
        return (location, customer, vehicle);
    }

    private static CreateAppointmentCommand CreateCommand(
        Guid workshopLocationId,
        Guid customerId,
        Guid vehicleId,
        DateTimeOffset? scheduledStartUtc = null,
        DateTimeOffset? scheduledEndUtc = null) =>
        new()
        {
            WorkshopLocationId = workshopLocationId,
            CustomerId = customerId,
            VehicleId = vehicleId,
            ScheduledStartUtc = scheduledStartUtc ?? DefaultStart,
            ScheduledEndUtc = scheduledEndUtc ?? DefaultEnd,
        };
}

[Collection(PostgreSqlCollection.Name)]
public sealed class AppointmentManagerAuthorizationTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task AppointmentManager_AllowsOwner()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Owner, shouldSucceed: true);
    }

    [Fact]
    public async Task AppointmentManager_AllowsAdministrator()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Administrator, shouldSucceed: true);
    }

    [Fact]
    public async Task AppointmentManager_AllowsServiceAdvisor()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.ServiceAdvisor, shouldSucceed: true);
    }

    [Fact]
    public async Task AppointmentManager_RejectsTechnician()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Technician, shouldSucceed: false);
    }

    [Fact]
    public async Task AppointmentManager_RejectsViewer()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Viewer, shouldSucceed: false);
    }

    [Fact]
    public async Task AppointmentManager_ReflectsDatabaseRoleChange()
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
        var handler = new AppointmentManagerAuthorizationHandler(writeScope, organizationContext);

        var initialContext = new AuthorizationHandlerContext(
            [new AppointmentManagerRequirement()],
            CreatePrincipal(advisor.Id),
            resource: null);
        await handler.HandleAsync(initialContext);
        Assert.True(initialContext.HasSucceeded);

        var advisorMembership = await writeScope.OrganizationMemberships
            .SingleAsync(membership => membership.UserId == advisor.Id);
        advisorMembership.ChangeRole(OrganizationMembershipRole.Technician);
        await writeScope.SaveChangesAsync();

        var afterContext = new AuthorizationHandlerContext(
            [new AppointmentManagerRequirement()],
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
        var handler = new AppointmentManagerAuthorizationHandler(writeScope, organizationContext);

        var context = new AuthorizationHandlerContext(
            [new AppointmentManagerRequirement()],
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
