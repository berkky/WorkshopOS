using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Estimates;
using WorkshopOS.Application.RepairOrders;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Infrastructure.Estimates;
using WorkshopOS.Infrastructure.Inspections;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class EstimateTenantIsolationTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public void Estimate_IsTenantFiltered()
    {
        using var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(Estimate)));
    }

    [Fact]
    public void EstimateItem_IsTenantFiltered()
    {
        using var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(EstimateItem)));
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class EstimateManagementTests(PostgreSqlTestFixture fixture)
{
    private static readonly DateTimeOffset DefaultNow = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EstimateManagement_Create_UsesCurrentOrganization()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            manager.Id,
            OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateEstimateAsync(manager.Id, scenario.RepairOrder.Id);

        Assert.True(result.Success);
        var estimate = await writeScope.Estimates.SingleAsync(candidate => candidate.Id == result.Value);
        Assert.Equal(organization.Id, estimate.OrganizationId);
        Assert.Equal(scenario.RepairOrder.Id, estimate.RepairOrderId);
        Assert.Equal(EstimateStatus.Draft, estimate.Status);
    }

    [Fact]
    public async Task EstimateManagement_Create_WhenOrganizationUnresolved_FailsClosed()
    {
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var service = CreateService(scope.Context, new UnresolvedOrganizationContext(), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateEstimateAsync(Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.OrganizationUnresolved, result.FailureReason);
        Assert.Equal(0, await scope.Context.Estimates.CountAsync());
    }

    [Fact]
    public async Task EstimateManagement_Create_RejectsOtherTenantRepairOrder()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, manager.Id, OrganizationMembershipRole.Owner);

        Guid repairOrderBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var scenarioB = await CreateEligibleRepairOrderAsync(scopeB, organizationB.Id, suffix);
            repairOrderBId = scenarioB.RepairOrder.Id;
        }

        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = CreateService(writeScopeA, new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateEstimateAsync(manager.Id, repairOrderBId);

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.RepairOrderNotFound, result.FailureReason);
        Assert.Equal(0, await writeScopeA.Estimates.CountAsync());
    }

    [Fact]
    public async Task EstimateManagement_Create_RejectsCancelledRepairOrder()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id));
        var scenario = await CreateEligibleRepairOrderAsync(
            writeScope,
            organization.Id,
            suffix,
            RepairOrderStatus.Cancelled);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateEstimateAsync(manager.Id, scenario.RepairOrder.Id);

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.RepairOrderNotEligible, result.FailureReason);
        Assert.True(RepairOrderLifecyclePolicy.IsTerminal(RepairOrderStatus.Cancelled));
    }

    [Fact]
    public async Task EstimateManagement_Create_RejectsCompletedRepairOrder()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id));
        var scenario = await CreateEligibleRepairOrderAsync(
            writeScope,
            organization.Id,
            suffix,
            RepairOrderStatus.Completed);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateEstimateAsync(manager.Id, scenario.RepairOrder.Id);

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.RepairOrderNotEligible, result.FailureReason);
        Assert.True(RepairOrderLifecyclePolicy.IsTerminal(RepairOrderStatus.Completed));
    }

    [Fact]
    public async Task EstimateManagement_Create_UsesServerGeneratedNumber()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateEstimateAsync(manager.Id, scenario.RepairOrder.Id);

        Assert.True(result.Success);
        var estimate = await writeScope.Estimates.SingleAsync(candidate => candidate.Id == result.Value);
        Assert.Matches(@"^EST-\d{8}-[0-9A-F]{8}$", estimate.Number);
        Assert.StartsWith($"EST-{DefaultNow:yyyyMMdd}-", estimate.Number, StringComparison.Ordinal);
        Assert.NotEqual($"EST-{DefaultNow:yyyyMMdd}-MANUAL", estimate.Number);
    }

    [Fact]
    public async Task EstimateManagement_Create_UsesOrganizationCurrencySnapshot()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateEstimateAsync(manager.Id, scenario.RepairOrder.Id);

        Assert.True(result.Success);
        var estimate = await writeScope.Estimates.SingleAsync(candidate => candidate.Id == result.Value);
        Assert.Equal("TRY", estimate.CurrencyCode);
    }

    [Fact]
    public async Task EstimateItems_DraftCanAddItem()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = (await service.CreateEstimateAsync(manager.Id, scenario.RepairOrder.Id)).Value!;

        var result = await service.AddEstimateItemAsync(
            manager.Id,
            new AddEstimateItemCommand
            {
                EstimateId = estimateId,
                Description = "Brake pads",
                Quantity = 2,
                UnitPrice = 150m,
            });

        Assert.True(result.Success);
        var item = await writeScope.EstimateItems.SingleAsync(candidate => candidate.Id == result.Value);
        Assert.Equal(estimateId, item.EstimateId);
        Assert.Equal("Brake pads", item.Description);
    }

    [Fact]
    public async Task EstimateItems_DraftCanEditItem()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = (await service.CreateEstimateAsync(manager.Id, scenario.RepairOrder.Id)).Value!;
        var itemId = await AddItemAsync(service, manager.Id, estimateId, "Original", 1, 100m);

        var result = await service.UpdateEstimateItemAsync(
            manager.Id,
            new UpdateEstimateItemCommand
            {
                EstimateId = estimateId,
                EstimateItemId = itemId,
                Description = "Updated",
                Quantity = 2,
                UnitPrice = 125m,
            });

        Assert.True(result.Success);
        var item = await writeScope.EstimateItems.SingleAsync(candidate => candidate.Id == itemId);
        Assert.Equal("Updated", item.Description);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(125m, item.UnitPrice);
    }

    [Fact]
    public async Task EstimateItems_DraftCanRemoveItem()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = (await service.CreateEstimateAsync(manager.Id, scenario.RepairOrder.Id)).Value!;
        var itemId = await AddItemAsync(service, manager.Id, estimateId, "Remove me", 1, 50m);

        var result = await service.RemoveEstimateItemAsync(manager.Id, estimateId, itemId);

        Assert.True(result.Success);
        Assert.Equal(0, await writeScope.EstimateItems.CountAsync(candidate => candidate.EstimateId == estimateId));
    }

    [Fact]
    public async Task EstimateItems_RejectsZeroOrNegativeQuantity()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = (await service.CreateEstimateAsync(manager.Id, scenario.RepairOrder.Id)).Value!;

        var zeroResult = await service.AddEstimateItemAsync(
            manager.Id,
            new AddEstimateItemCommand
            {
                EstimateId = estimateId,
                Description = "Zero quantity",
                Quantity = 0,
                UnitPrice = 10m,
            });

        var negativeResult = await service.AddEstimateItemAsync(
            manager.Id,
            new AddEstimateItemCommand
            {
                EstimateId = estimateId,
                Description = "Negative quantity",
                Quantity = -1,
                UnitPrice = 10m,
            });

        Assert.False(zeroResult.Success);
        Assert.Equal(EstimateOperationFailureReason.InvalidInput, zeroResult.FailureReason);
        Assert.False(negativeResult.Success);
        Assert.Equal(EstimateOperationFailureReason.InvalidInput, negativeResult.FailureReason);
    }

    [Fact]
    public async Task EstimateItems_RejectsNegativeUnitPrice()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = (await service.CreateEstimateAsync(manager.Id, scenario.RepairOrder.Id)).Value!;

        var result = await service.AddEstimateItemAsync(
            manager.Id,
            new AddEstimateItemCommand
            {
                EstimateId = estimateId,
                Description = "Negative price",
                Quantity = 1,
                UnitPrice = -0.01m,
            });

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.InvalidInput, result.FailureReason);
    }

    [Fact]
    public async Task EstimateItems_AllowsZeroUnitPrice()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = (await service.CreateEstimateAsync(manager.Id, scenario.RepairOrder.Id)).Value!;

        var result = await service.AddEstimateItemAsync(
            manager.Id,
            new AddEstimateItemCommand
            {
                EstimateId = estimateId,
                Description = "Complimentary check",
                Quantity = 1,
                UnitPrice = 0m,
            });

        Assert.True(result.Success);
        var item = await writeScope.EstimateItems.SingleAsync(candidate => candidate.Id == result.Value);
        Assert.Equal(0m, item.UnitPrice);
    }

    [Fact]
    public async Task EstimateItems_RejectsOtherEstimateItem()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenarioA = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, $"{suffix}-a");
        var scenarioB = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, $"{suffix}-b");
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateAId = (await service.CreateEstimateAsync(manager.Id, scenarioA.RepairOrder.Id)).Value!;
        var estimateBId = (await service.CreateEstimateAsync(manager.Id, scenarioB.RepairOrder.Id)).Value!;
        var itemAId = await AddItemAsync(service, manager.Id, estimateAId, "Estimate A item", 1, 10m);
        await AddItemAsync(service, manager.Id, estimateBId, "Estimate B item", 1, 20m);

        var result = await service.UpdateEstimateItemAsync(
            manager.Id,
            new UpdateEstimateItemCommand
            {
                EstimateId = estimateBId,
                EstimateItemId = itemAId,
                Description = "Cross estimate",
                Quantity = 1,
                UnitPrice = 99m,
            });

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.ItemNotFound, result.FailureReason);
    }

    [Fact]
    public async Task EstimateItems_RejectsOtherTenantItem()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);

        Guid foreignItemId;
        Guid foreignEstimateId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow)))
        {
            var managerB = await TestDataFactory.PersistUserAsync(scopeB, $"{suffix}-b");
            await TestDataFactory.PersistMembershipAsync(scopeB, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);
            var scenarioB = await CreateEligibleRepairOrderAsync(scopeB, organizationB.Id, suffix);
            var serviceB = CreateService(scopeB, new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow));
            foreignEstimateId = (await serviceB.CreateEstimateAsync(managerB.Id, scenarioB.RepairOrder.Id)).Value!;
            foreignItemId = await AddItemAsync(serviceB, managerB.Id, foreignEstimateId, "Tenant B item", 1, 10m);
        }

        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        var scenarioA = await CreateEligibleRepairOrderAsync(writeScopeA, organizationA.Id, suffix);
        var serviceA = CreateService(writeScopeA, new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        var estimateAId = (await serviceA.CreateEstimateAsync(managerA.Id, scenarioA.RepairOrder.Id)).Value!;

        var result = await serviceA.UpdateEstimateItemAsync(
            managerA.Id,
            new UpdateEstimateItemCommand
            {
                EstimateId = estimateAId,
                EstimateItemId = foreignItemId,
                Description = "Should fail",
                Quantity = 1,
                UnitPrice = 10m,
            });

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.ItemNotFound, result.FailureReason);

        var removeResult = await serviceA.RemoveEstimateItemAsync(managerA.Id, foreignEstimateId, foreignItemId);
        Assert.False(removeResult.Success);
        Assert.Equal(EstimateOperationFailureReason.EstimateNotFound, removeResult.FailureReason);
    }

    [Fact]
    public async Task EstimateItems_ClientCannotControlOrganizationId()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = (await service.CreateEstimateAsync(manager.Id, scenario.RepairOrder.Id)).Value!;

        var itemId = await AddItemAsync(service, manager.Id, estimateId, "Server owned", 1, 25m);
        var item = await writeScope.EstimateItems.SingleAsync(candidate => candidate.Id == itemId);

        Assert.Equal(organization.Id, item.OrganizationId);
        Assert.Equal(organization.Id, (await writeScope.Estimates.SingleAsync(candidate => candidate.Id == estimateId)).OrganizationId);
    }

    [Fact]
    public async Task EstimateItems_TotalIsServerCalculated()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = (await service.CreateEstimateAsync(manager.Id, scenario.RepairOrder.Id)).Value!;
        await AddItemAsync(service, manager.Id, estimateId, "Line 1", 1, 10.125m);
        await AddItemAsync(service, manager.Id, estimateId, "Line 2", 1, 10.125m);

        var details = await service.GetEstimateDetailsAsync(estimateId);

        Assert.NotNull(details);
        Assert.Equal(20.26m, details.Total);
        Assert.All(details.Items, item =>
            Assert.Equal(EstimateMoneyCalculator.CalculateLineTotal(item.Quantity, item.UnitPrice), item.LineTotal));
    }

    [Fact]
    public async Task EstimateItems_MaximumItemLimitEnforced()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = (await service.CreateEstimateAsync(manager.Id, scenario.RepairOrder.Id)).Value!;

        for (var index = 0; index < EstimateInputValidator.MaxItemsPerEstimate; index++)
        {
            var result = await service.AddEstimateItemAsync(
                manager.Id,
                new AddEstimateItemCommand
                {
                    EstimateId = estimateId,
                    Description = $"Item {index}",
                    Quantity = 1,
                    UnitPrice = 1m,
                });
            Assert.True(result.Success);
        }

        var overflowResult = await service.AddEstimateItemAsync(
            manager.Id,
            new AddEstimateItemCommand
            {
                EstimateId = estimateId,
                Description = "Overflow",
                Quantity = 1,
                UnitPrice = 1m,
            });

        Assert.False(overflowResult.Success);
        Assert.Equal(EstimateOperationFailureReason.ItemLimitExceeded, overflowResult.FailureReason);
        Assert.Equal(EstimateInputValidator.MaxItemsPerEstimate, await writeScope.EstimateItems.CountAsync(candidate => candidate.EstimateId == estimateId));
    }

    [Fact]
    public void EstimateMoney_LineTotalUsesDocumentedRounding()
    {
        Assert.Equal(10.13m, EstimateMoneyCalculator.CalculateLineTotal(1, 10.125m));
        Assert.Equal(1.01m, EstimateMoneyCalculator.CalculateLineTotal(3, 0.335m));
        Assert.Equal(0.00m, EstimateMoneyCalculator.CalculateLineTotal(1, 0.004m));
    }

    [Fact]
    public void EstimateMoney_EstimateTotalIsSumOfRoundedLineTotals()
    {
        var items = new List<(decimal Quantity, decimal UnitPrice)>
        {
            (1, 10.125m),
            (1, 10.125m),
            (3, 0.335m),
        };

        var expected = items
            .Select(item => EstimateMoneyCalculator.CalculateLineTotal(item.Quantity, item.UnitPrice))
            .Sum();

        Assert.Equal(21.27m, EstimateMoneyCalculator.CalculateEstimateTotal(items));
        Assert.Equal(expected, EstimateMoneyCalculator.CalculateEstimateTotal(items));
    }

    [Fact]
    public async Task EstimateLifecycle_DraftWithItemsCanBePresented()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = (await service.CreateEstimateAsync(manager.Id, scenario.RepairOrder.Id)).Value!;
        await AddItemAsync(service, manager.Id, estimateId, "Presentable item", 1, 100m);

        var result = await service.PresentForApprovalAsync(manager.Id, estimateId);

        Assert.True(result.Success);
        var estimate = await writeScope.Estimates.SingleAsync(candidate => candidate.Id == estimateId);
        Assert.Equal(EstimateStatus.Sent, estimate.Status);
    }

    [Fact]
    public async Task EstimateLifecycle_EmptyDraftCannotBePresented()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = (await service.CreateEstimateAsync(manager.Id, scenario.RepairOrder.Id)).Value!;

        var result = await service.PresentForApprovalAsync(manager.Id, estimateId);

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.EmptyEstimate, result.FailureReason);
    }

    [Fact]
    public async Task EstimateLifecycle_PresentMakesFinancialContentsImmutable()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = (await service.CreateEstimateAsync(manager.Id, scenario.RepairOrder.Id)).Value!;
        var itemId = await AddItemAsync(service, manager.Id, estimateId, "Locked item", 1, 100m);
        await service.PresentForApprovalAsync(manager.Id, estimateId);

        var addResult = await service.AddEstimateItemAsync(
            manager.Id,
            new AddEstimateItemCommand
            {
                EstimateId = estimateId,
                Description = "New item",
                Quantity = 1,
                UnitPrice = 1m,
            });
        var updateResult = await service.UpdateEstimateItemAsync(
            manager.Id,
            new UpdateEstimateItemCommand
            {
                EstimateId = estimateId,
                EstimateItemId = itemId,
                Description = "Changed",
                Quantity = 2,
                UnitPrice = 200m,
            });
        var removeResult = await service.RemoveEstimateItemAsync(manager.Id, estimateId, itemId);
        var messageResult = await service.UpdateEstimateCustomerMessageAsync(manager.Id, estimateId, "Updated message");

        Assert.False(addResult.Success);
        Assert.Equal(EstimateOperationFailureReason.InvalidLifecycleTransition, addResult.FailureReason);
        Assert.False(updateResult.Success);
        Assert.Equal(EstimateOperationFailureReason.InvalidLifecycleTransition, updateResult.FailureReason);
        Assert.False(removeResult.Success);
        Assert.Equal(EstimateOperationFailureReason.InvalidLifecycleTransition, removeResult.FailureReason);
        Assert.False(messageResult.Success);
        Assert.Equal(EstimateOperationFailureReason.InvalidLifecycleTransition, messageResult.FailureReason);
    }

    [Fact]
    public async Task EstimateLifecycle_PresentSetsTimestamp()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = (await service.CreateEstimateAsync(manager.Id, scenario.RepairOrder.Id)).Value!;
        await AddItemAsync(service, manager.Id, estimateId, "Item", 1, 10m);

        await service.PresentForApprovalAsync(manager.Id, estimateId);

        var estimate = await writeScope.Estimates.SingleAsync(candidate => candidate.Id == estimateId);
        Assert.Equal(DefaultNow, estimate.SentAtUtc);
    }

    [Fact]
    public async Task EstimateLifecycle_CannotPresentTerminalEstimate()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var approvedEstimateId = await CreateApprovedEstimateAsync(service, manager.Id, scenario.RepairOrder.Id);
        var declinedEstimateId = await CreateDeclinedEstimateAsync(service, manager.Id, scenario.RepairOrder.Id);

        var approvedResult = await service.PresentForApprovalAsync(manager.Id, approvedEstimateId);
        var declinedResult = await service.PresentForApprovalAsync(manager.Id, declinedEstimateId);

        Assert.False(approvedResult.Success);
        Assert.Equal(EstimateOperationFailureReason.InvalidLifecycleTransition, approvedResult.FailureReason);
        Assert.False(declinedResult.Success);
        Assert.Equal(EstimateOperationFailureReason.InvalidLifecycleTransition, declinedResult.FailureReason);
        Assert.True(EstimateLifecyclePolicy.IsTerminal(EstimateStatus.Approved));
        Assert.True(EstimateLifecyclePolicy.IsTerminal(EstimateStatus.Declined));
    }

    [Fact]
    public async Task EstimateLifecycle_PendingEstimateCanRecordApproval()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = await CreatePresentedEstimateAsync(service, manager.Id, scenario.RepairOrder.Id);

        var result = await service.RecordCustomerApprovalAsync(manager.Id, estimateId);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task EstimateLifecycle_ApprovalMarksApproved()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = await CreatePresentedEstimateAsync(service, manager.Id, scenario.RepairOrder.Id);

        await service.RecordCustomerApprovalAsync(manager.Id, estimateId);

        var estimate = await writeScope.Estimates.SingleAsync(candidate => candidate.Id == estimateId);
        Assert.Equal(EstimateStatus.Approved, estimate.Status);
    }

    [Fact]
    public async Task EstimateLifecycle_ApprovalSetsTimestamp()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = await CreatePresentedEstimateAsync(service, manager.Id, scenario.RepairOrder.Id);

        await service.RecordCustomerApprovalAsync(manager.Id, estimateId);

        var estimate = await writeScope.Estimates.SingleAsync(candidate => candidate.Id == estimateId);
        Assert.Equal(DefaultNow, estimate.ApprovedAtUtc);
    }

    [Fact]
    public async Task EstimateLifecycle_ApprovedEstimateIsImmutable()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = await CreateApprovedEstimateAsync(service, manager.Id, scenario.RepairOrder.Id);
        var itemId = await writeScope.EstimateItems
            .Where(candidate => candidate.EstimateId == estimateId)
            .Select(candidate => candidate.Id)
            .SingleAsync();

        var addResult = await service.AddEstimateItemAsync(
            manager.Id,
            new AddEstimateItemCommand
            {
                EstimateId = estimateId,
                Description = "Blocked",
                Quantity = 1,
                UnitPrice = 1m,
            });
        var updateResult = await service.UpdateEstimateItemAsync(
            manager.Id,
            new UpdateEstimateItemCommand
            {
                EstimateId = estimateId,
                EstimateItemId = itemId,
                Description = "Blocked",
                Quantity = 2,
                UnitPrice = 2m,
            });
        var removeResult = await service.RemoveEstimateItemAsync(manager.Id, estimateId, itemId);
        var presentResult = await service.PresentForApprovalAsync(manager.Id, estimateId);

        Assert.False(addResult.Success);
        Assert.False(updateResult.Success);
        Assert.False(removeResult.Success);
        Assert.False(presentResult.Success);
    }

    [Fact]
    public async Task EstimateLifecycle_ApprovedCannotBeDeclined()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = await CreateApprovedEstimateAsync(service, manager.Id, scenario.RepairOrder.Id);

        var result = await service.RecordCustomerDeclineAsync(manager.Id, estimateId);

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.InvalidLifecycleTransition, result.FailureReason);
    }

    [Fact]
    public async Task EstimateLifecycle_PendingEstimateCanRecordDecline()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = await CreatePresentedEstimateAsync(service, manager.Id, scenario.RepairOrder.Id);

        var result = await service.RecordCustomerDeclineAsync(manager.Id, estimateId);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task EstimateLifecycle_DeclineMarksDeclined()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = await CreatePresentedEstimateAsync(service, manager.Id, scenario.RepairOrder.Id);

        await service.RecordCustomerDeclineAsync(manager.Id, estimateId);

        var estimate = await writeScope.Estimates.SingleAsync(candidate => candidate.Id == estimateId);
        Assert.Equal(EstimateStatus.Declined, estimate.Status);
    }

    [Fact]
    public async Task EstimateLifecycle_DeclineSetsTimestamp()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = await CreatePresentedEstimateAsync(service, manager.Id, scenario.RepairOrder.Id);

        await service.RecordCustomerDeclineAsync(manager.Id, estimateId);

        var estimate = await writeScope.Estimates.SingleAsync(candidate => candidate.Id == estimateId);
        Assert.Equal(DefaultNow, estimate.DeclinedAtUtc);
    }

    [Fact]
    public async Task EstimateLifecycle_DeclinedEstimateIsImmutable()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = await CreateDeclinedEstimateAsync(service, manager.Id, scenario.RepairOrder.Id);
        var itemId = await writeScope.EstimateItems
            .Where(candidate => candidate.EstimateId == estimateId)
            .Select(candidate => candidate.Id)
            .SingleAsync();

        var addResult = await service.AddEstimateItemAsync(
            manager.Id,
            new AddEstimateItemCommand
            {
                EstimateId = estimateId,
                Description = "Blocked",
                Quantity = 1,
                UnitPrice = 1m,
            });
        var updateResult = await service.UpdateEstimateItemAsync(
            manager.Id,
            new UpdateEstimateItemCommand
            {
                EstimateId = estimateId,
                EstimateItemId = itemId,
                Description = "Blocked",
                Quantity = 2,
                UnitPrice = 2m,
            });
        var removeResult = await service.RemoveEstimateItemAsync(manager.Id, estimateId, itemId);
        var presentResult = await service.PresentForApprovalAsync(manager.Id, estimateId);

        Assert.False(addResult.Success);
        Assert.False(updateResult.Success);
        Assert.False(removeResult.Success);
        Assert.False(presentResult.Success);
    }

    [Fact]
    public async Task EstimateLifecycle_DeclinedCannotBeApproved()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = await CreateDeclinedEstimateAsync(service, manager.Id, scenario.RepairOrder.Id);

        var result = await service.RecordCustomerApprovalAsync(manager.Id, estimateId);

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.InvalidLifecycleTransition, result.FailureReason);
    }

    [Fact]
    public async Task EstimateManagement_List_ReturnsOnlyCurrentOrganizationEstimates()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);

        Guid estimateBId;
        await using (var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow)))
        {
            var scenarioA = await CreateEligibleRepairOrderAsync(scopeA, organizationA.Id, $"{suffix}-a");
            var serviceA = CreateService(scopeA, new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
            await serviceA.CreateEstimateAsync(managerA.Id, scenarioA.RepairOrder.Id);
        }

        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow)))
        {
            var managerB = await TestDataFactory.PersistUserAsync(scopeB, $"{suffix}-b");
            await TestDataFactory.PersistMembershipAsync(scopeB, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);
            var scenarioB = await CreateEligibleRepairOrderAsync(scopeB, organizationB.Id, $"{suffix}-b");
            var serviceB = CreateService(scopeB, new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow));
            estimateBId = (await serviceB.CreateEstimateAsync(managerB.Id, scenarioB.RepairOrder.Id)).Value!;
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = CreateService(readScopeA, new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        var result = await service.ListEstimatesAsync(new EstimateListQuery());

        Assert.Equal(1, result.TotalCount);
        Assert.DoesNotContain(result.Items, item => item.EstimateId == estimateBId);
    }

    [Fact]
    public async Task EstimateManagement_Details_DoesNotExposeOtherTenantEstimate()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid estimateBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow)))
        {
            var managerB = await TestDataFactory.PersistUserAsync(scopeB, $"{suffix}-b");
            await TestDataFactory.PersistMembershipAsync(scopeB, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);
            var scenarioB = await CreateEligibleRepairOrderAsync(scopeB, organizationB.Id, suffix);
            var serviceB = CreateService(scopeB, new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow));
            estimateBId = (await serviceB.CreateEstimateAsync(managerB.Id, scenarioB.RepairOrder.Id)).Value!;
        }

        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = CreateService(readScopeA, new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));

        var details = await service.GetEstimateDetailsAsync(estimateBId);
        Assert.Null(details);
    }

    [Fact]
    public async Task EstimateManagement_Edit_DoesNotModifyOtherTenantEstimate()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid estimateBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow)))
        {
            var managerB = await TestDataFactory.PersistUserAsync(scopeB, $"{suffix}-b");
            await TestDataFactory.PersistMembershipAsync(scopeB, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);
            var scenarioB = await CreateEligibleRepairOrderAsync(scopeB, organizationB.Id, suffix);
            var serviceB = CreateService(scopeB, new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow));
            estimateBId = (await serviceB.CreateEstimateAsync(managerB.Id, scenarioB.RepairOrder.Id)).Value!;
            await AddItemAsync(serviceB, managerB.Id, estimateBId, "Tenant B item", 1, 10m);
        }

        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);
        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        var service = CreateService(writeScopeA, new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.UpdateEstimateCustomerMessageAsync(managerA.Id, estimateBId, "Cross-tenant edit");

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.EstimateNotFound, result.FailureReason);
    }

    [Fact]
    public async Task EstimateManagement_Present_DoesNotModifyOtherTenantEstimate()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid estimateBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow)))
        {
            var managerB = await TestDataFactory.PersistUserAsync(scopeB, $"{suffix}-b");
            await TestDataFactory.PersistMembershipAsync(scopeB, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);
            var scenarioB = await CreateEligibleRepairOrderAsync(scopeB, organizationB.Id, suffix);
            var serviceB = CreateService(scopeB, new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow));
            estimateBId = (await serviceB.CreateEstimateAsync(managerB.Id, scenarioB.RepairOrder.Id)).Value!;
            await AddItemAsync(serviceB, managerB.Id, estimateBId, "Tenant B item", 1, 10m);
        }

        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);
        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        var service = CreateService(writeScopeA, new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.PresentForApprovalAsync(managerA.Id, estimateBId);

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.EstimateNotFound, result.FailureReason);
        await using var verifyScopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        var estimateB = await verifyScopeB.Estimates.SingleAsync(candidate => candidate.Id == estimateBId);
        Assert.Equal(EstimateStatus.Draft, estimateB.Status);
    }

    [Fact]
    public async Task EstimateManagement_RecordApproval_DoesNotModifyOtherTenantEstimate()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid estimateBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow)))
        {
            var managerB = await TestDataFactory.PersistUserAsync(scopeB, $"{suffix}-b");
            await TestDataFactory.PersistMembershipAsync(scopeB, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);
            var scenarioB = await CreateEligibleRepairOrderAsync(scopeB, organizationB.Id, suffix);
            var serviceB = CreateService(scopeB, new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow));
            estimateBId = await CreatePresentedEstimateAsync(serviceB, managerB.Id, scenarioB.RepairOrder.Id);
        }

        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);
        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        var service = CreateService(writeScopeA, new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.RecordCustomerApprovalAsync(managerA.Id, estimateBId);

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.EstimateNotFound, result.FailureReason);
        await using var verifyScopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        var estimateB = await verifyScopeB.Estimates.SingleAsync(candidate => candidate.Id == estimateBId);
        Assert.Equal(EstimateStatus.Sent, estimateB.Status);
    }

    [Fact]
    public async Task EstimateManagement_RecordDecline_DoesNotModifyOtherTenantEstimate()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid estimateBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow)))
        {
            var managerB = await TestDataFactory.PersistUserAsync(scopeB, $"{suffix}-b");
            await TestDataFactory.PersistMembershipAsync(scopeB, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);
            var scenarioB = await CreateEligibleRepairOrderAsync(scopeB, organizationB.Id, suffix);
            var serviceB = CreateService(scopeB, new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow));
            estimateBId = await CreatePresentedEstimateAsync(serviceB, managerB.Id, scenarioB.RepairOrder.Id);
        }

        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);
        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        var service = CreateService(writeScopeA, new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.RecordCustomerDeclineAsync(managerA.Id, estimateBId);

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.EstimateNotFound, result.FailureReason);
        await using var verifyScopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        var estimateB = await verifyScopeB.Estimates.SingleAsync(candidate => candidate.Id == estimateBId);
        Assert.Equal(EstimateStatus.Sent, estimateB.Status);
    }

    [Fact]
    public async Task EstimateLifecycle_Present_DoesNotChangeRepairOrderStatus()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix, RepairOrderStatus.InProgress);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = (await service.CreateEstimateAsync(manager.Id, scenario.RepairOrder.Id)).Value!;
        await AddItemAsync(service, manager.Id, estimateId, "Item", 1, 10m);

        await service.PresentForApprovalAsync(manager.Id, estimateId);

        var repairOrder = await writeScope.RepairOrders.SingleAsync(candidate => candidate.Id == scenario.RepairOrder.Id);
        Assert.Equal(RepairOrderStatus.InProgress, repairOrder.Status);
    }

    [Fact]
    public async Task EstimateLifecycle_Approval_DoesNotCompleteRepairOrder()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix, RepairOrderStatus.InProgress);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = await CreatePresentedEstimateAsync(service, manager.Id, scenario.RepairOrder.Id);

        await service.RecordCustomerApprovalAsync(manager.Id, estimateId);

        var repairOrder = await writeScope.RepairOrders.SingleAsync(candidate => candidate.Id == scenario.RepairOrder.Id);
        Assert.Equal(RepairOrderStatus.InProgress, repairOrder.Status);
    }

    [Fact]
    public async Task EstimateLifecycle_Decline_DoesNotCancelRepairOrder()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix, RepairOrderStatus.InProgress);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var estimateId = await CreatePresentedEstimateAsync(service, manager.Id, scenario.RepairOrder.Id);

        await service.RecordCustomerDeclineAsync(manager.Id, estimateId);

        var repairOrder = await writeScope.RepairOrders.SingleAsync(candidate => candidate.Id == scenario.RepairOrder.Id);
        Assert.Equal(RepairOrderStatus.InProgress, repairOrder.Status);
        Assert.NotEqual(RepairOrderStatus.Cancelled, repairOrder.Status);
    }

    [Fact]
    public async Task EstimateLifecycle_Approval_DoesNotAlterInspectionEvidence()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var inspectionService = new InspectionManagementService(
            writeScope,
            new TestOrganizationContext(organization.Id),
            new FakeTimeProvider(DefaultNow));
        var estimateService = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));

        var inspectionId = (await inspectionService.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id)).Value!;
        await inspectionService.StartInspectionAsync(manager.Id, inspectionId);
        var inspectionItem = await writeScope.InspectionItems.FirstAsync(candidate => candidate.InspectionId == inspectionId);
        await inspectionService.UpdateInspectionItemsAsync(
            manager.Id,
            new Application.Inspections.UpdateInspectionItemsCommand
            {
                InspectionId = inspectionId,
                Items =
                [
                    new Application.Inspections.InspectionItemUpdate
                    {
                        InspectionItemId = inspectionItem.Id,
                        Condition = InspectionCondition.Critical,
                        Notes = "Worn pads",
                    },
                ],
            });

        var before = await writeScope.InspectionItems.SingleAsync(candidate => candidate.Id == inspectionItem.Id);
        var estimateId = await CreatePresentedEstimateAsync(estimateService, manager.Id, scenario.RepairOrder.Id);
        await estimateService.RecordCustomerApprovalAsync(manager.Id, estimateId);
        var after = await writeScope.InspectionItems.SingleAsync(candidate => candidate.Id == inspectionItem.Id);

        Assert.Equal(before.Condition, after.Condition);
        Assert.Equal(before.Notes, after.Notes);
        Assert.Equal(InspectionCondition.Critical, after.Condition);
    }

    private static IEstimateManagementService CreateService(
        AppDbContext context,
        IOrganizationContext organizationContext,
        TimeProvider timeProvider) =>
        new EstimateManagementService(
            context,
            organizationContext,
            new EstimateNumberGenerator(),
            timeProvider);

    private static async Task<EligibleRepairOrderScenario> CreateEligibleRepairOrderAsync(
        AppDbContext context,
        Guid organizationId,
        string suffix,
        RepairOrderStatus status = RepairOrderStatus.Draft)
    {
        var location = await TestDataFactory.PersistWorkshopLocationAsync(context, organizationId, suffix);
        var customer = await TestDataFactory.PersistCustomerAsync(context, organizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(context, organizationId, suffix, customer.Id);
        var repairOrder = await TestDataFactory.PersistRepairOrderAsync(
            context,
            organizationId,
            location.Id,
            customer.Id,
            vehicle.Id,
            suffix,
            DefaultNow,
            status);

        return new EligibleRepairOrderScenario(location, repairOrder);
    }

    private static async Task<Guid> AddItemAsync(
        IEstimateManagementService service,
        Guid actorUserId,
        Guid estimateId,
        string description,
        decimal quantity,
        decimal unitPrice)
    {
        var result = await service.AddEstimateItemAsync(
            actorUserId,
            new AddEstimateItemCommand
            {
                EstimateId = estimateId,
                Description = description,
                Quantity = quantity,
                UnitPrice = unitPrice,
            });

        Assert.True(result.Success);
        return result.Value!;
    }

    private static async Task<Guid> CreatePresentedEstimateAsync(
        IEstimateManagementService service,
        Guid actorUserId,
        Guid repairOrderId)
    {
        var estimateId = (await service.CreateEstimateAsync(actorUserId, repairOrderId)).Value!;
        await AddItemAsync(service, actorUserId, estimateId, "Presented item", 1, 100m);
        await service.PresentForApprovalAsync(actorUserId, estimateId);
        return estimateId;
    }

    private static async Task<Guid> CreateApprovedEstimateAsync(
        IEstimateManagementService service,
        Guid actorUserId,
        Guid repairOrderId)
    {
        var estimateId = await CreatePresentedEstimateAsync(service, actorUserId, repairOrderId);
        await service.RecordCustomerApprovalAsync(actorUserId, estimateId);
        return estimateId;
    }

    private static async Task<Guid> CreateDeclinedEstimateAsync(
        IEstimateManagementService service,
        Guid actorUserId,
        Guid repairOrderId)
    {
        var estimateId = await CreatePresentedEstimateAsync(service, actorUserId, repairOrderId);
        await service.RecordCustomerDeclineAsync(actorUserId, estimateId);
        return estimateId;
    }

    private sealed record EligibleRepairOrderScenario(
        Domain.Organizations.WorkshopLocation Location,
        RepairOrder RepairOrder);
}

[Collection(PostgreSqlCollection.Name)]
public sealed class EstimateManagerAuthorizationTests(PostgreSqlTestFixture fixture)
{
    [Theory(DisplayName = "EstimateManager_AllowsOwner/Administrator/ServiceAdvisor")]
    [InlineData(OrganizationMembershipRole.Owner)]
    [InlineData(OrganizationMembershipRole.Administrator)]
    [InlineData(OrganizationMembershipRole.ServiceAdvisor)]
    public async Task EstimateManager_AllowsManagerRoles(OrganizationMembershipRole role)
    {
        await AssertRoleAllowed(role, shouldSucceed: true);
    }

    [Theory(DisplayName = "EstimateManager_RejectsTechnician/Viewer")]
    [InlineData(OrganizationMembershipRole.Technician)]
    [InlineData(OrganizationMembershipRole.Viewer)]
    public async Task EstimateManager_RejectsNonManagerRoles(OrganizationMembershipRole role)
    {
        await AssertRoleAllowed(role, shouldSucceed: false);
    }

    [Fact]
    public async Task EstimateManager_ReflectsDatabaseRoleChange()
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
        var handler = new EstimateManagerAuthorizationHandler(writeScope, organizationContext);

        var initialContext = new AuthorizationHandlerContext(
            [new EstimateManagerRequirement()],
            CreatePrincipal(advisor.Id),
            resource: null);
        await handler.HandleAsync(initialContext);
        Assert.True(initialContext.HasSucceeded);

        var advisorMembership = await writeScope.OrganizationMemberships
            .SingleAsync(membership => membership.UserId == advisor.Id);
        advisorMembership.ChangeRole(OrganizationMembershipRole.Technician);
        await writeScope.SaveChangesAsync();

        var afterContext = new AuthorizationHandlerContext(
            [new EstimateManagerRequirement()],
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
        var handler = new EstimateManagerAuthorizationHandler(writeScope, organizationContext);

        var context = new AuthorizationHandlerContext(
            [new EstimateManagerRequirement()],
            CreatePrincipal(user.Id),
            resource: null);

        await handler.HandleAsync(context);
        Assert.Equal(shouldSucceed, context.HasSucceeded);
        Assert.Equal(shouldSucceed, EstimateManagerPolicy.CanManageEstimates(role));
    }

    private static System.Security.Claims.ClaimsPrincipal CreatePrincipal(Guid userId) =>
        new(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString())],
            authenticationType: "Test"));
}
