using Microsoft.EntityFrameworkCore;
using WorkshopOS.Domain.Appointments;
using WorkshopOS.Domain.Customers;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Domain.Vehicles;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class CompositeForeignKeyTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task CompositeForeignKey_RejectsCrossTenantVehicleCustomer()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var vehicleA = await TestDataFactory.PersistVehicleAsync(scopeA, organizationA.Id, suffix);
        scopeA.Entry(vehicleA).Property<Guid?>(nameof(Vehicle.CurrentCustomerId)).CurrentValue = customerB.Id;

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task CompositeForeignKey_RejectsCrossTenantAppointmentVehicle()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var start = new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(1);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix);

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var locationA = await TestDataFactory.PersistWorkshopLocationAsync(scopeA, organizationA.Id, suffix);
        var customerA = await TestDataFactory.PersistCustomerAsync(scopeA, organizationA.Id, suffix);

        scopeA.Appointments.Add(new Appointment(
            organizationA.Id,
            locationA.Id,
            customerA.Id,
            vehicleB.Id,
            start,
            end));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task CompositeForeignKey_RejectsCrossTenantRepairOrderCustomer()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var locationA = await TestDataFactory.PersistWorkshopLocationAsync(scopeA, organizationA.Id, suffix);
        var vehicleA = await TestDataFactory.PersistVehicleAsync(scopeA, organizationA.Id, suffix);

        scopeA.RepairOrders.Add(new RepairOrder(
            organizationA.Id,
            locationA.Id,
            customerB.Id,
            vehicleA.Id,
            $"RO-{suffix}",
            openedAt));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }
}
