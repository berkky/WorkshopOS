using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Inventory;
using WorkshopOS.Domain.Inventory;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class InventoryTenantIsolationTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public void PartInventoryBalance_IsTenantFiltered()
    {
        using var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(PartInventoryBalance)));
    }

    [Fact]
    public void PartInventoryMovement_IsTenantFiltered()
    {
        using var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(PartInventoryMovement)));
    }

    [Fact]
    public async Task PartInventoryBalance_IsTenantFiltered_DoesNotExposeOtherOrganizationBalances()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        var managerB = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-b");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);

        Guid balanceBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(CatalogTestSupport.DefaultNow)))
        {
            var partService = CatalogTestSupport.CreatePartCatalogService(scopeB, new TestOrganizationContext(organizationB.Id));
            var inventoryService = CatalogTestSupport.CreateInventoryService(
                scopeB,
                new TestOrganizationContext(organizationB.Id),
                new FakeTimeProvider(CatalogTestSupport.DefaultNow));
            var location = await CatalogTestSupport.CreateCatalogLocationScenarioAsync(scopeB, organizationB.Id, suffix);
            var partId = await CatalogTestSupport.CreatePartAsync(partService, managerB.Id, suffix);
            await CatalogTestSupport.AdjustInventoryAsync(
                inventoryService,
                managerB.Id,
                partId,
                location.Location.Id,
                PartInventoryMovementType.OpeningBalance,
                3m);
            balanceBId = await scopeB.PartInventoryBalances.Select(candidate => candidate.Id).SingleAsync();
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        Assert.Equal(0, await readScopeA.PartInventoryBalances.CountAsync());
        Assert.Null(await readScopeA.PartInventoryBalances.SingleOrDefaultAsync(candidate => candidate.Id == balanceBId));
    }

    [Fact]
    public async Task PartInventoryMovement_IsTenantFiltered_DoesNotExposeOtherOrganizationMovements()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        var managerB = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-b");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);

        Guid movementBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(CatalogTestSupport.DefaultNow)))
        {
            var partService = CatalogTestSupport.CreatePartCatalogService(scopeB, new TestOrganizationContext(organizationB.Id));
            var inventoryService = CatalogTestSupport.CreateInventoryService(
                scopeB,
                new TestOrganizationContext(organizationB.Id),
                new FakeTimeProvider(CatalogTestSupport.DefaultNow));
            var location = await CatalogTestSupport.CreateCatalogLocationScenarioAsync(scopeB, organizationB.Id, suffix);
            var partId = await CatalogTestSupport.CreatePartAsync(partService, managerB.Id, suffix);
            await CatalogTestSupport.AdjustInventoryAsync(
                inventoryService,
                managerB.Id,
                partId,
                location.Location.Id,
                PartInventoryMovementType.OpeningBalance,
                3m);
            movementBId = await scopeB.PartInventoryMovements.Select(candidate => candidate.Id).SingleAsync();
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        Assert.Equal(0, await readScopeA.PartInventoryMovements.CountAsync());
        Assert.Null(await readScopeA.PartInventoryMovements.SingleOrDefaultAsync(candidate => candidate.Id == movementBId));
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class InventoryManagementTests(PostgreSqlTestFixture fixture)
{
    private static readonly DateTimeOffset DefaultNow = CatalogTestSupport.DefaultNow;

    [Fact]
    public async Task Inventory_Adjustment_UsesCurrentOrganization()
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

        var result = await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            4m);

        Assert.True(result.Success);
        var balance = await writeScope.PartInventoryBalances.SingleAsync();
        Assert.Equal(scenario.OrganizationId, balance.OrganizationId);
        Assert.Equal(4m, balance.QuantityOnHand);
    }

    [Fact]
    public async Task Inventory_Adjustment_WhenOrganizationUnresolvedFailsClosed()
    {
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var inventoryService = CatalogTestSupport.CreateInventoryService(
            scope.Context,
            new UnresolvedOrganizationContext(),
            new FakeTimeProvider(DefaultNow));

        var result = await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            PartInventoryMovementType.ManualIncrease,
            1m);

        Assert.False(result.Success);
        Assert.Equal(InventoryAdjustmentFailureReason.OrganizationUnresolved, result.FailureReason);
        Assert.Equal(0, await scope.Context.PartInventoryBalances.CountAsync());
        Assert.Equal(0, await scope.Context.PartInventoryMovements.CountAsync());
    }

    [Fact]
    public async Task Inventory_Adjustment_CreatesBalanceOnFirstAdjustment()
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

        Assert.Equal(0, await writeScope.PartInventoryBalances.CountAsync());

        var result = await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            2m);

        Assert.True(result.Success);
        Assert.Equal(1, await writeScope.PartInventoryBalances.CountAsync());
        Assert.Equal(2m, result.BalanceAfter);
    }

    [Fact]
    public async Task Inventory_Adjustment_IncreaseUpdatesBalance()
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

        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            2m);

        var result = await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.ManualIncrease,
            3m);

        Assert.True(result.Success);
        Assert.Equal(5m, result.BalanceAfter);
        Assert.Equal(5m, await inventoryService.GetQuantityOnHandAsync(partId, location.Location.Id));
    }

    [Fact]
    public async Task Inventory_Adjustment_DecreaseUpdatesBalance()
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

        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            8m);

        var result = await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.ManualDecrease,
            3m);

        Assert.True(result.Success);
        Assert.Equal(5m, result.BalanceAfter);
    }

    [Fact]
    public async Task Inventory_Adjustment_CreatesImmutableMovement()
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

        var result = await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            6m,
            reason: "Initial stock");

        Assert.True(result.Success);
        var movement = await writeScope.PartInventoryMovements.SingleAsync();
        Assert.Equal(PartInventoryMovementType.OpeningBalance, movement.MovementType);
        Assert.Equal(6m, movement.QuantityDelta);
        Assert.Equal("Initial stock", movement.Reason);
    }

    [Fact]
    public async Task Inventory_Adjustment_RecordsBalanceAfter()
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

        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            4m);

        var result = await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.ManualDecrease,
            1m);

        Assert.True(result.Success);
        var movement = await writeScope.PartInventoryMovements
            .SingleAsync(candidate => candidate.MovementType == PartInventoryMovementType.ManualDecrease);
        Assert.Equal(3m, movement.BalanceAfter);
    }

    [Fact]
    public async Task Inventory_Adjustment_RecordsServerActor()
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

        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            1m);

        var movement = await writeScope.PartInventoryMovements.SingleAsync();
        Assert.Equal(scenario.ManagerId, movement.RecordedByUserId);
        Assert.Equal(DefaultNow, movement.OccurredAtUtc);
    }

    [Fact]
    public async Task Inventory_Adjustment_RejectsOtherTenantPart()
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
        Guid locationAId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var partService = CatalogTestSupport.CreatePartCatalogService(scopeB, new TestOrganizationContext(organizationB.Id));
            partBId = await CatalogTestSupport.CreatePartAsync(partService, managerB.Id, suffix);
        }

        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        locationAId = (await CatalogTestSupport.CreateCatalogLocationScenarioAsync(writeScopeA, organizationA.Id, suffix)).Location.Id;
        var inventoryService = CatalogTestSupport.CreateInventoryService(
            writeScopeA,
            new TestOrganizationContext(organizationA.Id),
            new FakeTimeProvider(DefaultNow));

        var result = await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            managerA.Id,
            partBId,
            locationAId,
            PartInventoryMovementType.ManualIncrease,
            1m);

        Assert.False(result.Success);
        Assert.Equal(InventoryAdjustmentFailureReason.PartNotFound, result.FailureReason);
    }

    [Fact]
    public async Task Inventory_Adjustment_RejectsOtherTenantLocation()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);

        Guid locationBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            locationBId = (await CatalogTestSupport.CreateCatalogLocationScenarioAsync(scopeB, organizationB.Id, suffix)).Location.Id;
        }

        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        var partService = CatalogTestSupport.CreatePartCatalogService(writeScopeA, new TestOrganizationContext(organizationA.Id));
        var inventoryService = CatalogTestSupport.CreateInventoryService(
            writeScopeA,
            new TestOrganizationContext(organizationA.Id),
            new FakeTimeProvider(DefaultNow));
        var partId = await CatalogTestSupport.CreatePartAsync(partService, managerA.Id, suffix);

        var result = await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            managerA.Id,
            partId,
            locationBId,
            PartInventoryMovementType.ManualIncrease,
            1m);

        Assert.False(result.Success);
        Assert.Equal(InventoryAdjustmentFailureReason.LocationNotFound, result.FailureReason);
    }

    [Fact]
    public async Task Inventory_Adjustment_RejectsInactivePart()
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
        await partService.SetPartActiveStateAsync(scenario.ManagerId, partId, isActive: false);

        var result = await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.ManualIncrease,
            1m);

        Assert.False(result.Success);
        Assert.Equal(InventoryAdjustmentFailureReason.PartInactive, result.FailureReason);
    }

    [Fact]
    public async Task Inventory_Adjustment_RejectsInactiveLocation()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, clock);
        var partService = CatalogTestSupport.CreatePartCatalogService(writeScope, organizationContext);
        var inventoryService = CatalogTestSupport.CreateInventoryService(writeScope, organizationContext, clock);
        var inactiveLocation = await CatalogTestSupport.PersistInactiveLocationAsync(writeScope, scenario.OrganizationId, suffix);
        var partId = await CatalogTestSupport.CreatePartAsync(partService, scenario.ManagerId, suffix);

        var result = await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            inactiveLocation.Id,
            PartInventoryMovementType.ManualIncrease,
            1m);

        Assert.False(result.Success);
        Assert.Equal(InventoryAdjustmentFailureReason.LocationInactive, result.FailureReason);
    }

    [Fact]
    public async Task Inventory_Adjustment_RejectsZeroQuantity()
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

        var result = await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.ManualIncrease,
            0m);

        Assert.False(result.Success);
        Assert.Equal(InventoryAdjustmentFailureReason.InvalidInput, result.FailureReason);
    }

    [Fact]
    public async Task Inventory_Adjustment_RejectsNegativeResult()
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

        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            2m);

        var result = await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.ManualDecrease,
            5m);

        Assert.False(result.Success);
        Assert.Equal(InventoryAdjustmentFailureReason.InsufficientStock, result.FailureReason);
        Assert.Equal(2m, await inventoryService.GetQuantityOnHandAsync(partId, location.Location.Id));
    }

    [Fact]
    public async Task Inventory_MovementHistory_PreservesAllAdjustments()
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

        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            10m);
        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.ManualDecrease,
            2m);
        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.ManualIncrease,
            1m);

        var history = await inventoryService.GetMovementHistoryAsync(
            new InventoryMovementHistoryQuery
            {
                PartCatalogItemId = partId,
                WorkshopLocationId = location.Location.Id,
            });

        Assert.Equal(3, history.TotalCount);
        Assert.Equal(3, history.Items.Count);
        Assert.Equal(9m, history.Items[0].BalanceAfter);
        Assert.Equal(8m, history.Items[1].BalanceAfter);
        Assert.Equal(10m, history.Items[2].BalanceAfter);
    }

    [Fact]
    public async Task Inventory_MovementHistory_IsTenantScoped()
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
        Guid locationBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow)))
        {
            var partService = CatalogTestSupport.CreatePartCatalogService(scopeB, new TestOrganizationContext(organizationB.Id));
            var inventoryService = CatalogTestSupport.CreateInventoryService(
                scopeB,
                new TestOrganizationContext(organizationB.Id),
                new FakeTimeProvider(DefaultNow));
            var location = await CatalogTestSupport.CreateCatalogLocationScenarioAsync(scopeB, organizationB.Id, suffix);
            partBId = await CatalogTestSupport.CreatePartAsync(partService, managerB.Id, suffix);
            locationBId = location.Location.Id;
            await CatalogTestSupport.AdjustInventoryAsync(
                inventoryService,
                managerB.Id,
                partBId,
                locationBId,
                PartInventoryMovementType.OpeningBalance,
                4m);
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        var inventoryServiceA = CatalogTestSupport.CreateInventoryService(
            readScopeA,
            new TestOrganizationContext(organizationA.Id),
            new FakeTimeProvider(DefaultNow));
        var history = await inventoryServiceA.GetMovementHistoryAsync(
            new InventoryMovementHistoryQuery
            {
                PartCatalogItemId = partBId,
                WorkshopLocationId = locationBId,
            });

        Assert.Empty(history.Items);
        Assert.Equal(0, history.TotalCount);
    }

    [Fact]
    public async Task Inventory_MovementCannotBeEdited()
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

        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            5m);

        var originalMovement = await writeScope.PartInventoryMovements.AsNoTracking().SingleAsync();
        var originalDelta = originalMovement.QuantityDelta;
        var originalBalanceAfter = originalMovement.BalanceAfter;

        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.ManualIncrease,
            1m);

        var storedMovement = await writeScope.PartInventoryMovements
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == originalMovement.Id);
        Assert.Equal(originalDelta, storedMovement.QuantityDelta);
        Assert.Equal(originalBalanceAfter, storedMovement.BalanceAfter);
    }

    [Fact]
    public async Task Inventory_MovementCannotBeDeletedThroughService()
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

        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            5m);
        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.ManualDecrease,
            1m);

        Assert.Equal(2, await writeScope.PartInventoryMovements.CountAsync());
        Assert.Equal(4m, await CatalogTestSupport.GetTotalInventoryQuantityAsync(writeScope));
    }

    [Fact]
    public async Task Inventory_Adjustment_StaleConcurrentDecreaseFailsWhenInsufficientStock()
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

        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            10m);

        var firstDecrease = await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.ManualDecrease,
            7m);
        Assert.True(firstDecrease.Success);
        Assert.Equal(3m, firstDecrease.BalanceAfter);

        var secondDecrease = await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.ManualDecrease,
            5m);

        Assert.False(secondDecrease.Success);
        Assert.Equal(InventoryAdjustmentFailureReason.InsufficientStock, secondDecrease.FailureReason);
        Assert.Equal(3m, await inventoryService.GetQuantityOnHandAsync(partId, location.Location.Id));
        Assert.Equal(2, await writeScope.PartInventoryMovements.CountAsync());
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class InventoryManagerAuthorizationTests(PostgreSqlTestFixture fixture)
{
    [Theory(DisplayName = "InventoryManager_AllowsOwner/Administrator")]
    [InlineData(OrganizationMembershipRole.Owner)]
    [InlineData(OrganizationMembershipRole.Administrator)]
    public async Task InventoryManager_AllowsManagerRoles(OrganizationMembershipRole role)
    {
        await AssertRoleAllowed(role, shouldSucceed: true);
    }

    [Theory(DisplayName = "InventoryManager_RejectsServiceAdvisor/Technician/Viewer")]
    [InlineData(OrganizationMembershipRole.ServiceAdvisor)]
    [InlineData(OrganizationMembershipRole.Technician)]
    [InlineData(OrganizationMembershipRole.Viewer)]
    public async Task InventoryManager_RejectsNonManagerRoles(OrganizationMembershipRole role)
    {
        await AssertRoleAllowed(role, shouldSucceed: false);
    }

    [Fact]
    public async Task InventoryManager_ReflectsDatabaseRoleChange()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var owner = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner");
        var admin = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-admin");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, owner.Id, OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            admin.Id,
            OrganizationMembershipRole.Administrator);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext);
        var handler = new InventoryManagerAuthorizationHandler(writeScope, organizationContext);

        var initialContext = new AuthorizationHandlerContext(
            [new InventoryManagerRequirement()],
            CreatePrincipal(admin.Id),
            resource: null);
        await handler.HandleAsync(initialContext);
        Assert.True(initialContext.HasSucceeded);

        var adminMembership = await writeScope.OrganizationMemberships
            .SingleAsync(membership => membership.UserId == admin.Id);
        adminMembership.ChangeRole(OrganizationMembershipRole.ServiceAdvisor);
        await writeScope.SaveChangesAsync();

        var afterContext = new AuthorizationHandlerContext(
            [new InventoryManagerRequirement()],
            CreatePrincipal(admin.Id),
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
        var handler = new InventoryManagerAuthorizationHandler(writeScope, organizationContext);

        var context = new AuthorizationHandlerContext(
            [new InventoryManagerRequirement()],
            CreatePrincipal(user.Id),
            resource: null);

        await handler.HandleAsync(context);
        Assert.Equal(shouldSucceed, context.HasSucceeded);
        Assert.Equal(shouldSucceed, InventoryManagerPolicy.CanManageInventory(role));
    }

    private static System.Security.Claims.ClaimsPrincipal CreatePrincipal(Guid userId) =>
        new(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString())],
            authenticationType: "Test"));
}
