using Microsoft.EntityFrameworkCore;
using WorkshopOS.Domain.Appointments;
using WorkshopOS.Domain.Customers;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Domain.Vehicles;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PersistenceGuardTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task UtcGuard_RejectsNonZeroOffsetUtcProperty()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 9, 0, 0, TimeSpan.FromHours(3));

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id));
        var location = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, organization.Id, suffix);
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, organization.Id, suffix);

        writeScope.RepairOrders.Add(new RepairOrder(
            organization.Id,
            location.Id,
            customer.Id,
            vehicle.Id,
            $"RO-{suffix}",
            openedAt));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writeScope.SaveChangesAsync());
        Assert.Contains("UTC offset zero", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UtcGuard_AcceptsZeroOffsetUtcProperty()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 9, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id));
        var location = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, organization.Id, suffix);
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, organization.Id, suffix);

        writeScope.RepairOrders.Add(new RepairOrder(
            organization.Id,
            location.Id,
            customer.Id,
            vehicle.Id,
            $"RO-{suffix}",
            openedAt));

        await writeScope.SaveChangesAsync();
    }

    [Fact]
    public async Task SaveChanges_PreservesCreatedAtUtcOnModification()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var initialTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var updatedTime = initialTime.AddHours(2);
        var timeProvider = new FakeTimeProvider(initialTime);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext(), timeProvider);
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), timeProvider);
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, suffix);
        var createdAtUtc = customer.CreatedAtUtc;
        var updatedAtUtc = customer.UpdatedAtUtc;

        timeProvider.Advance(TimeSpan.FromHours(2));
        writeScope.Entry(customer).Property(nameof(Customer.DisplayName)).CurrentValue = "Updated customer";
        await writeScope.SaveChangesAsync();

        Assert.Equal(createdAtUtc, customer.CreatedAtUtc);
        Assert.True(customer.UpdatedAtUtc >= updatedAtUtc);
        Assert.Equal(updatedTime, customer.UpdatedAtUtc);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ModelMetadataTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public void TenantOwnedEntities_HaveOrganizationFilter()
    {
        using var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);

        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(WorkshopLocation)));
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(Customer)));
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(Vehicle)));
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(Appointment)));
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(RepairOrder)));
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(Inspection)));
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(InspectionItem)));
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(Estimate)));
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(EstimateItem)));
    }
}
