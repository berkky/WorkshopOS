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
public sealed class TenantQueryFilterTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task UnresolvedOrganizationContext_ReturnsNoTenantRows()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);

        await using (var writeContext = scope.CreateContext(new TestOrganizationContext(organization.Id)))
        {
            await TestDataFactory.PersistCustomerAsync(writeContext, organization.Id, suffix);
        }

        await using (var readContext = scope.CreateContext(new UnresolvedOrganizationContext()))
        {
            var customers = await readContext.Customers.ToListAsync();
            Assert.Empty(customers);
        }
    }

    [Fact]
    public async Task OrganizationFilter_ReturnsOnlyCurrentOrganizationRows()
    {
        var suffixA = Guid.CreateVersion7().ToString("N");
        var suffixB = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffixA);
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffixB);

        await using (var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id)))
        {
            await TestDataFactory.PersistCustomerAsync(scopeA, organizationA.Id, suffixA);
        }

        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffixB);
        }

        await using (var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id)))
        {
            var customersA = await readScopeA.Customers.ToListAsync();
            Assert.Single(customersA);
            Assert.Equal(organizationA.Id, customersA[0].OrganizationId);
        }

        await using (var readScopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var customersB = await readScopeB.Customers.ToListAsync();
            Assert.Single(customersB);
            Assert.Equal(organizationB.Id, customersB[0].OrganizationId);
        }
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class TenantWriteGuardTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task TenantWriteGuard_RejectsMismatchedOrganizationId()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        Guid organizationAId;
        Guid organizationBId;

        await using var setupScope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        organizationAId = (await TestDataFactory.PersistOrganizationAsync(setupScope.Context, $"{suffix}-a")).Id;
        organizationBId = (await TestDataFactory.PersistOrganizationAsync(setupScope.Context, $"{suffix}-b")).Id;

        await using var scope = setupScope.CreateContext(new TestOrganizationContext(organizationAId));
        scope.Customers.Add(new Customer(organizationBId, "Cross-tenant customer"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.SaveChangesAsync());
        Assert.Contains("belong to the resolved organization", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TenantWriteGuard_RejectsWriteWhenOrganizationIsUnresolved()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        Guid organizationId;

        await using var setupScope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        organizationId = (await TestDataFactory.PersistOrganizationAsync(setupScope.Context, suffix)).Id;

        await using var scope = setupScope.CreateContext(new UnresolvedOrganizationContext());
        scope.Customers.Add(new Customer(organizationId, "Unresolved tenant write"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.SaveChangesAsync());
        Assert.Contains("Organization context must be resolved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
