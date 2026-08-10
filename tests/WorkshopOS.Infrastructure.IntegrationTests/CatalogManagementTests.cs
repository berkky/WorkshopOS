using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Catalog;
using WorkshopOS.Application.Estimates;
using WorkshopOS.Domain.Catalog;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class CatalogTenantIsolationTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public void ServiceCatalogItem_IsTenantFiltered()
    {
        using var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(ServiceCatalogItem)));
    }

    [Fact]
    public void PartCatalogItem_IsTenantFiltered()
    {
        using var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(PartCatalogItem)));
    }

    [Fact]
    public async Task ServiceCatalogItem_IsTenantFiltered_DoesNotExposeOtherOrganizationItems()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        var managerB = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-b");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);

        Guid serviceBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var service = CatalogTestSupport.CreateServiceCatalogService(scopeB, new TestOrganizationContext(organizationB.Id));
            serviceBId = await CatalogTestSupport.CreateServiceAsync(service, managerB.Id, suffix);
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        Assert.Equal(0, await readScopeA.ServiceCatalogItems.CountAsync());
        Assert.Null(await readScopeA.ServiceCatalogItems.SingleOrDefaultAsync(candidate => candidate.Id == serviceBId));
    }

    [Fact]
    public async Task PartCatalogItem_IsTenantFiltered_DoesNotExposeOtherOrganizationItems()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        var managerB = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-b");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);

        Guid partBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var service = CatalogTestSupport.CreatePartCatalogService(scopeB, new TestOrganizationContext(organizationB.Id));
            partBId = await CatalogTestSupport.CreatePartAsync(service, managerB.Id, suffix);
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        Assert.Equal(0, await readScopeA.PartCatalogItems.CountAsync());
        Assert.Null(await readScopeA.PartCatalogItems.SingleOrDefaultAsync(candidate => candidate.Id == partBId));
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class CatalogManagementTests(PostgreSqlTestFixture fixture)
{
    private static readonly DateTimeOffset DefaultNow = CatalogTestSupport.DefaultNow;

    [Fact]
    public async Task ServiceCatalog_Create_UsesCurrentOrganization()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);

        await using var writeScope = scope.CreateContext(organizationContext);
        var service = CatalogTestSupport.CreateServiceCatalogService(writeScope, organizationContext);
        var serviceId = await CatalogTestSupport.CreateServiceAsync(service, scenario.ManagerId, suffix);

        var item = await writeScope.ServiceCatalogItems.SingleAsync(candidate => candidate.Id == serviceId);
        Assert.Equal(scenario.OrganizationId, item.OrganizationId);
    }

    [Fact]
    public async Task ServiceCatalog_Create_WhenOrganizationUnresolvedFailsClosed()
    {
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var service = CatalogTestSupport.CreateServiceCatalogService(scope.Context, new UnresolvedOrganizationContext());

        var result = await service.CreateServiceAsync(
            Guid.CreateVersion7(),
            new CreateServiceCatalogItemCommand
            {
                Code = "SVC-UNRESOLVED",
                Name = "Unresolved service",
                DefaultUnitPrice = 10m,
            });

        Assert.False(result.Success);
        Assert.Equal(CatalogOperationFailureReason.OrganizationUnresolved, result.FailureReason);
        Assert.Equal(0, await scope.Context.ServiceCatalogItems.CountAsync());
    }

    [Fact]
    public async Task ServiceCatalog_CodeIsNormalized()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);

        await using var writeScope = scope.CreateContext(organizationContext);
        var service = CatalogTestSupport.CreateServiceCatalogService(writeScope, organizationContext);
        var serviceId = await CatalogTestSupport.CreateServiceAsync(service, scenario.ManagerId, suffix, code: "  svc-alpha  ");

        var item = await writeScope.ServiceCatalogItems.SingleAsync(candidate => candidate.Id == serviceId);
        Assert.Equal("SVC-ALPHA", item.Code);
    }

    [Fact]
    public async Task ServiceCatalog_CurrencyComesFromOrganization()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);

        await using var writeScope = scope.CreateContext(organizationContext);
        var organization = await writeScope.Organizations.SingleAsync(candidate => candidate.Id == scenario.OrganizationId);
        var service = CatalogTestSupport.CreateServiceCatalogService(writeScope, organizationContext);
        var serviceId = await CatalogTestSupport.CreateServiceAsync(service, scenario.ManagerId, suffix);

        var item = await writeScope.ServiceCatalogItems.SingleAsync(candidate => candidate.Id == serviceId);
        Assert.Equal(organization.DefaultCurrencyCode, item.CurrencyCode);
    }

    [Fact]
    public async Task ServiceCatalog_Update_UpdatesSameTenantOnly()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);

        Guid serviceBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var managerB = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-b");
            await TestDataFactory.PersistMembershipAsync(scope.Context, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);
            var serviceB = CatalogTestSupport.CreateServiceCatalogService(scopeB, new TestOrganizationContext(organizationB.Id));
            serviceBId = await CatalogTestSupport.CreateServiceAsync(serviceB, managerB.Id, suffix);
        }

        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var serviceA = CatalogTestSupport.CreateServiceCatalogService(writeScopeA, new TestOrganizationContext(organizationA.Id));
        var result = await serviceA.UpdateServiceAsync(
            managerA.Id,
            new UpdateServiceCatalogItemCommand
            {
                ServiceCatalogItemId = serviceBId,
                Code = "HIJACKED",
                Name = "Hijacked",
                DefaultUnitPrice = 1m,
                IsActive = true,
            });

        Assert.False(result.Success);
        Assert.Equal(CatalogOperationFailureReason.ItemNotFound, result.FailureReason);
    }

    [Fact]
    public async Task ServiceCatalog_Deactivate_PreservesRow()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);

        await using var writeScope = scope.CreateContext(organizationContext);
        var service = CatalogTestSupport.CreateServiceCatalogService(writeScope, organizationContext);
        var serviceId = await CatalogTestSupport.CreateServiceAsync(service, scenario.ManagerId, suffix);

        var deactivateResult = await service.SetServiceActiveStateAsync(scenario.ManagerId, serviceId, isActive: false);
        Assert.True(deactivateResult.Success);

        var item = await writeScope.ServiceCatalogItems.SingleAsync(candidate => candidate.Id == serviceId);
        Assert.False(item.IsActive);
        Assert.Equal(1, await writeScope.ServiceCatalogItems.CountAsync());
    }

    [Fact]
    public async Task ServiceCatalog_InactiveItemCannotBeAddedToEstimate()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, clock);
        var catalogService = CatalogTestSupport.CreateServiceCatalogService(writeScope, organizationContext);
        var estimateService = CatalogTestSupport.CreateEstimateService(writeScope, organizationContext, clock);
        var serviceId = await CatalogTestSupport.CreateServiceAsync(catalogService, scenario.ManagerId, suffix);
        await catalogService.SetServiceActiveStateAsync(scenario.ManagerId, serviceId, isActive: false);
        var estimateId = await CatalogTestSupport.CreateDraftEstimateAsync(
            estimateService,
            writeScope,
            scenario.OrganizationId,
            scenario.ManagerId,
            suffix);

        var result = await estimateService.AddServiceCatalogItemAsync(
            scenario.ManagerId,
            new AddServiceCatalogItemToEstimateCommand
            {
                EstimateId = estimateId,
                ServiceCatalogItemId = serviceId,
                Quantity = 1,
            });

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.CatalogItemInactive, result.FailureReason);
    }

    [Fact]
    public async Task ServiceCatalog_SearchIsTenantScoped()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var sharedName = $"Shared Service {suffix}";

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        var managerB = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-b");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);

        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var serviceB = CatalogTestSupport.CreateServiceCatalogService(scopeB, new TestOrganizationContext(organizationB.Id));
            await CatalogTestSupport.CreateServiceAsync(serviceB, managerB.Id, suffix, name: sharedName);
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var serviceA = CatalogTestSupport.CreateServiceCatalogService(readScopeA, new TestOrganizationContext(organizationA.Id));
        var result = await serviceA.ListServicesAsync(new ServiceCatalogListQuery { Search = sharedName });

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task PartCatalog_Create_UsesCurrentOrganization()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);

        await using var writeScope = scope.CreateContext(organizationContext);
        var service = CatalogTestSupport.CreatePartCatalogService(writeScope, organizationContext);
        var partId = await CatalogTestSupport.CreatePartAsync(service, scenario.ManagerId, suffix);

        var item = await writeScope.PartCatalogItems.SingleAsync(candidate => candidate.Id == partId);
        Assert.Equal(scenario.OrganizationId, item.OrganizationId);
    }

    [Fact]
    public async Task PartCatalog_Create_WhenOrganizationUnresolvedFailsClosed()
    {
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var service = CatalogTestSupport.CreatePartCatalogService(scope.Context, new UnresolvedOrganizationContext());

        var result = await service.CreatePartAsync(
            Guid.CreateVersion7(),
            new CreatePartCatalogItemCommand
            {
                Sku = "PART-UNRESOLVED",
                Name = "Unresolved part",
                DefaultUnitPrice = 10m,
            });

        Assert.False(result.Success);
        Assert.Equal(CatalogOperationFailureReason.OrganizationUnresolved, result.FailureReason);
        Assert.Equal(0, await scope.Context.PartCatalogItems.CountAsync());
    }

    [Fact]
    public async Task PartCatalog_SkuIsNormalized()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);

        await using var writeScope = scope.CreateContext(organizationContext);
        var service = CatalogTestSupport.CreatePartCatalogService(writeScope, organizationContext);
        var partId = await CatalogTestSupport.CreatePartAsync(service, scenario.ManagerId, suffix, sku: "  part-alpha  ");

        var item = await writeScope.PartCatalogItems.SingleAsync(candidate => candidate.Id == partId);
        Assert.Equal("PART-ALPHA", item.Sku);
    }

    [Fact]
    public async Task PartCatalog_CurrencyComesFromOrganization()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);

        await using var writeScope = scope.CreateContext(organizationContext);
        var organization = await writeScope.Organizations.SingleAsync(candidate => candidate.Id == scenario.OrganizationId);
        var service = CatalogTestSupport.CreatePartCatalogService(writeScope, organizationContext);
        var partId = await CatalogTestSupport.CreatePartAsync(service, scenario.ManagerId, suffix);

        var item = await writeScope.PartCatalogItems.SingleAsync(candidate => candidate.Id == partId);
        Assert.Equal(organization.DefaultCurrencyCode, item.CurrencyCode);
    }

    [Fact]
    public async Task PartCatalog_Update_UpdatesSameTenantOnly()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);

        Guid partBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var managerB = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-b");
            await TestDataFactory.PersistMembershipAsync(scope.Context, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);
            var serviceB = CatalogTestSupport.CreatePartCatalogService(scopeB, new TestOrganizationContext(organizationB.Id));
            partBId = await CatalogTestSupport.CreatePartAsync(serviceB, managerB.Id, suffix);
        }

        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var serviceA = CatalogTestSupport.CreatePartCatalogService(writeScopeA, new TestOrganizationContext(organizationA.Id));
        var result = await serviceA.UpdatePartAsync(
            managerA.Id,
            new UpdatePartCatalogItemCommand
            {
                PartCatalogItemId = partBId,
                Sku = "HIJACKED",
                Name = "Hijacked",
                DefaultUnitPrice = 1m,
                IsActive = true,
            });

        Assert.False(result.Success);
        Assert.Equal(CatalogOperationFailureReason.ItemNotFound, result.FailureReason);
    }

    [Fact]
    public async Task PartCatalog_SearchIsTenantScoped()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var sharedName = $"Shared Part {suffix}";

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        var managerB = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-b");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);

        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var serviceB = CatalogTestSupport.CreatePartCatalogService(scopeB, new TestOrganizationContext(organizationB.Id));
            await CatalogTestSupport.CreatePartAsync(serviceB, managerB.Id, suffix, name: sharedName);
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var serviceA = CatalogTestSupport.CreatePartCatalogService(readScopeA, new TestOrganizationContext(organizationA.Id));
        var result = await serviceA.ListPartsAsync(new PartCatalogListQuery { Search = sharedName });

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task PartCatalog_CannotDeactivateWithPositiveStock()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, clock);
        var partService = CatalogTestSupport.CreatePartCatalogService(writeScope, organizationContext);
        var inventoryService = CatalogTestSupport.CreateInventoryService(writeScope, organizationContext, clock);
        var location = await CatalogTestSupport.CreateCatalogLocationScenarioAsync(writeScope, scenario.OrganizationId, suffix);
        var partId = await CatalogTestSupport.CreatePartAsync(partService, scenario.ManagerId, suffix);

        var adjustment = await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            Domain.Inventory.PartInventoryMovementType.OpeningBalance,
            5m);
        Assert.True(adjustment.Success);

        var result = await partService.SetPartActiveStateAsync(scenario.ManagerId, partId, isActive: false);

        Assert.False(result.Success);
        Assert.Equal(CatalogOperationFailureReason.HasPositiveStock, result.FailureReason);
        Assert.True(await writeScope.PartCatalogItems.Where(candidate => candidate.Id == partId).Select(candidate => candidate.IsActive).SingleAsync());
    }

    [Fact]
    public async Task PartCatalog_CanDeactivateWhenAllStockIsZero()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, clock);
        var partService = CatalogTestSupport.CreatePartCatalogService(writeScope, organizationContext);
        var inventoryService = CatalogTestSupport.CreateInventoryService(writeScope, organizationContext, clock);
        var location = await CatalogTestSupport.CreateCatalogLocationScenarioAsync(writeScope, scenario.OrganizationId, suffix);
        var partId = await CatalogTestSupport.CreatePartAsync(partService, scenario.ManagerId, suffix);

        var adjustment = await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            Domain.Inventory.PartInventoryMovementType.OpeningBalance,
            5m);
        Assert.True(adjustment.Success);

        var decrease = await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            Domain.Inventory.PartInventoryMovementType.ManualDecrease,
            5m);
        Assert.True(decrease.Success);

        var result = await partService.SetPartActiveStateAsync(scenario.ManagerId, partId, isActive: false);

        Assert.True(result.Success);
        Assert.False(await writeScope.PartCatalogItems.Where(candidate => candidate.Id == partId).Select(candidate => candidate.IsActive).SingleAsync());
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class CatalogManagerAuthorizationTests(PostgreSqlTestFixture fixture)
{
    [Theory(DisplayName = "CatalogManager_AllowsOwner/Administrator/ServiceAdvisor")]
    [InlineData(OrganizationMembershipRole.Owner)]
    [InlineData(OrganizationMembershipRole.Administrator)]
    [InlineData(OrganizationMembershipRole.ServiceAdvisor)]
    public async Task CatalogManager_AllowsManagerRoles(OrganizationMembershipRole role)
    {
        await AssertRoleAllowed(role, shouldSucceed: true);
    }

    [Theory(DisplayName = "CatalogManager_RejectsTechnician/Viewer")]
    [InlineData(OrganizationMembershipRole.Technician)]
    [InlineData(OrganizationMembershipRole.Viewer)]
    public async Task CatalogManager_RejectsNonManagerRoles(OrganizationMembershipRole role)
    {
        await AssertRoleAllowed(role, shouldSucceed: false);
    }

    [Fact]
    public async Task CatalogManager_ReflectsDatabaseRoleChange()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var owner = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner");
        var advisor = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-advisor");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, owner.Id, OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            advisor.Id,
            OrganizationMembershipRole.ServiceAdvisor);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext);
        var handler = new CatalogManagerAuthorizationHandler(writeScope, organizationContext);

        var initialContext = new AuthorizationHandlerContext(
            [new CatalogManagerRequirement()],
            CreatePrincipal(advisor.Id),
            resource: null);
        await handler.HandleAsync(initialContext);
        Assert.True(initialContext.HasSucceeded);

        var advisorMembership = await writeScope.OrganizationMemberships
            .SingleAsync(membership => membership.UserId == advisor.Id);
        advisorMembership.ChangeRole(OrganizationMembershipRole.Technician);
        await writeScope.SaveChangesAsync();

        var afterContext = new AuthorizationHandlerContext(
            [new CatalogManagerRequirement()],
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
        var handler = new CatalogManagerAuthorizationHandler(writeScope, organizationContext);

        var context = new AuthorizationHandlerContext(
            [new CatalogManagerRequirement()],
            CreatePrincipal(user.Id),
            resource: null);

        await handler.HandleAsync(context);
        Assert.Equal(shouldSucceed, context.HasSucceeded);
        Assert.Equal(shouldSucceed, CatalogManagerPolicy.CanManageCatalog(role));
    }

    private static System.Security.Claims.ClaimsPrincipal CreatePrincipal(Guid userId) =>
        new(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString())],
            authenticationType: "Test"));
}
