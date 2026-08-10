using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.CustomerPortal;
using WorkshopOS.Application.Estimates;
using WorkshopOS.Application.EstimateSharing;
using WorkshopOS.Application.Inspections;
using WorkshopOS.Application.Inventory;
using WorkshopOS.Application.RepairOrders;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.Inventory;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Infrastructure.Inspections;
using WorkshopOS.Infrastructure.Operations;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.RepairOrders;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class EstimateCatalogTests(PostgreSqlTestFixture fixture)
{
    private static readonly DateTimeOffset DefaultNow = CatalogTestSupport.DefaultNow;

    [Fact]
    public async Task EstimateCatalog_AddService_CreatesSnapshotLine()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, clock);
        var catalogService = CatalogTestSupport.CreateServiceCatalogService(writeScope, organizationContext);
        var estimateService = CatalogTestSupport.CreateEstimateService(writeScope, organizationContext, clock);
        var serviceId = await CatalogTestSupport.CreateServiceAsync(
            catalogService,
            scenario.ManagerId,
            suffix,
            price: 150m,
            name: "Brake Service");
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
                Quantity = 2,
            });

        Assert.True(result.Success);
        var item = await writeScope.EstimateItems.SingleAsync(candidate => candidate.Id == result.Value);
        Assert.Equal(EstimateItemType.Service, item.Type);
        Assert.Equal(150m, item.UnitPrice);
        Assert.Equal(2m, item.Quantity);
        Assert.Contains("Brake Service", item.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EstimateCatalog_AddPart_CreatesSnapshotLine()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, clock);
        var partService = CatalogTestSupport.CreatePartCatalogService(writeScope, organizationContext);
        var estimateService = CatalogTestSupport.CreateEstimateService(writeScope, organizationContext, clock);
        var partId = await CatalogTestSupport.CreatePartAsync(
            partService,
            scenario.ManagerId,
            suffix,
            sku: "BRK-001",
            price: 45m,
            name: "Brake Pad");
        var estimateId = await CatalogTestSupport.CreateDraftEstimateAsync(
            estimateService,
            writeScope,
            scenario.OrganizationId,
            scenario.ManagerId,
            suffix);

        var result = await estimateService.AddPartCatalogItemAsync(
            scenario.ManagerId,
            new AddPartCatalogItemToEstimateCommand
            {
                EstimateId = estimateId,
                PartCatalogItemId = partId,
                Quantity = 4,
            });

        Assert.True(result.Success);
        var item = await writeScope.EstimateItems.SingleAsync(candidate => candidate.Id == result.Value);
        Assert.Equal(EstimateItemType.Part, item.Type);
        Assert.Equal(45m, item.UnitPrice);
        Assert.Equal(4m, item.Quantity);
        Assert.Contains("Brake Pad", item.Description, StringComparison.Ordinal);
        Assert.Contains("BRK-001", item.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EstimateCatalog_RejectsInactiveService()
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
    public async Task EstimateCatalog_RejectsInactivePart()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, clock);
        var partService = CatalogTestSupport.CreatePartCatalogService(writeScope, organizationContext);
        var estimateService = CatalogTestSupport.CreateEstimateService(writeScope, organizationContext, clock);
        var partId = await CatalogTestSupport.CreatePartAsync(partService, scenario.ManagerId, suffix);
        await partService.SetPartActiveStateAsync(scenario.ManagerId, partId, isActive: false);
        var estimateId = await CatalogTestSupport.CreateDraftEstimateAsync(
            estimateService,
            writeScope,
            scenario.OrganizationId,
            scenario.ManagerId,
            suffix);

        var result = await estimateService.AddPartCatalogItemAsync(
            scenario.ManagerId,
            new AddPartCatalogItemToEstimateCommand
            {
                EstimateId = estimateId,
                PartCatalogItemId = partId,
                Quantity = 1,
            });

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.CatalogItemInactive, result.FailureReason);
    }

    [Fact]
    public async Task EstimateCatalog_RejectsOtherTenantService()
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
            var catalogService = CatalogTestSupport.CreateServiceCatalogService(scopeB, new TestOrganizationContext(organizationB.Id));
            serviceBId = await CatalogTestSupport.CreateServiceAsync(catalogService, managerB.Id, suffix);
        }

        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        var estimateService = CatalogTestSupport.CreateEstimateService(
            writeScopeA,
            new TestOrganizationContext(organizationA.Id),
            new FakeTimeProvider(DefaultNow));
        var estimateId = await CatalogTestSupport.CreateDraftEstimateAsync(
            estimateService,
            writeScopeA,
            organizationA.Id,
            managerA.Id,
            suffix);

        var result = await estimateService.AddServiceCatalogItemAsync(
            managerA.Id,
            new AddServiceCatalogItemToEstimateCommand
            {
                EstimateId = estimateId,
                ServiceCatalogItemId = serviceBId,
                Quantity = 1,
            });

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.CatalogItemNotFound, result.FailureReason);
    }

    [Fact]
    public async Task EstimateCatalog_RejectsOtherTenantPart()
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
            var partService = CatalogTestSupport.CreatePartCatalogService(scopeB, new TestOrganizationContext(organizationB.Id));
            partBId = await CatalogTestSupport.CreatePartAsync(partService, managerB.Id, suffix);
        }

        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        var estimateService = CatalogTestSupport.CreateEstimateService(
            writeScopeA,
            new TestOrganizationContext(organizationA.Id),
            new FakeTimeProvider(DefaultNow));
        var estimateId = await CatalogTestSupport.CreateDraftEstimateAsync(
            estimateService,
            writeScopeA,
            organizationA.Id,
            managerA.Id,
            suffix);

        var result = await estimateService.AddPartCatalogItemAsync(
            managerA.Id,
            new AddPartCatalogItemToEstimateCommand
            {
                EstimateId = estimateId,
                PartCatalogItemId = partBId,
                Quantity = 1,
            });

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.CatalogItemNotFound, result.FailureReason);
    }

    [Fact]
    public async Task EstimateCatalog_RejectsCurrencyMismatch()
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
        var estimateId = await CatalogTestSupport.CreateDraftEstimateAsync(
            estimateService,
            writeScope,
            scenario.OrganizationId,
            scenario.ManagerId,
            suffix);

        var estimate = await writeScope.Estimates.SingleAsync(candidate => candidate.Id == estimateId);
        writeScope.Entry(estimate).Property(nameof(Estimate.CurrencyCode)).CurrentValue = "USD";
        await writeScope.SaveChangesAsync();

        var result = await estimateService.AddServiceCatalogItemAsync(
            scenario.ManagerId,
            new AddServiceCatalogItemToEstimateCommand
            {
                EstimateId = estimateId,
                ServiceCatalogItemId = serviceId,
                Quantity = 1,
            });

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.CurrencyMismatch, result.FailureReason);
    }

    [Fact]
    public async Task EstimateCatalog_RequiresDraftEstimate()
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
        var scenarioRo = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, scenario.OrganizationId, $"{suffix}-ro");
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(
            estimateService,
            scenario.ManagerId,
            scenarioRo.RepairOrder.Id);

        var result = await estimateService.AddServiceCatalogItemAsync(
            scenario.ManagerId,
            new AddServiceCatalogItemToEstimateCommand
            {
                EstimateId = estimateId,
                ServiceCatalogItemId = serviceId,
                Quantity = 1,
            });

        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.InvalidLifecycleTransition, result.FailureReason);
    }

    [Fact]
    public async Task EstimateCatalog_CatalogPriceChangeDoesNotRewriteExistingEstimateItem()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, clock);
        var catalogService = CatalogTestSupport.CreateServiceCatalogService(writeScope, organizationContext);
        var estimateService = CatalogTestSupport.CreateEstimateService(writeScope, organizationContext, clock);
        var serviceId = await CatalogTestSupport.CreateServiceAsync(catalogService, scenario.ManagerId, suffix, price: 100m);
        var estimateId = await CatalogTestSupport.CreateDraftEstimateAsync(
            estimateService,
            writeScope,
            scenario.OrganizationId,
            scenario.ManagerId,
            suffix);
        var itemId = (await estimateService.AddServiceCatalogItemAsync(
            scenario.ManagerId,
            new AddServiceCatalogItemToEstimateCommand
            {
                EstimateId = estimateId,
                ServiceCatalogItemId = serviceId,
                Quantity = 1,
            })).Value!;

        await catalogService.UpdateServiceAsync(
            scenario.ManagerId,
            new Application.Catalog.UpdateServiceCatalogItemCommand
            {
                ServiceCatalogItemId = serviceId,
                Code = $"SVC-{suffix}",
                Name = "Updated Service",
                DefaultUnitPrice = 250m,
                IsActive = true,
            });

        var item = await writeScope.EstimateItems.SingleAsync(candidate => candidate.Id == itemId);
        Assert.Equal(100m, item.UnitPrice);
    }

    [Fact]
    public async Task EstimateCatalog_CatalogDescriptionChangeDoesNotRewriteExistingEstimateItem()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, clock);
        var partService = CatalogTestSupport.CreatePartCatalogService(writeScope, organizationContext);
        var estimateService = CatalogTestSupport.CreateEstimateService(writeScope, organizationContext, clock);
        var partId = await CatalogTestSupport.CreatePartAsync(
            partService,
            scenario.ManagerId,
            suffix,
            sku: "FLT-001",
            name: "Oil Filter");
        var estimateId = await CatalogTestSupport.CreateDraftEstimateAsync(
            estimateService,
            writeScope,
            scenario.OrganizationId,
            scenario.ManagerId,
            suffix);
        var itemId = (await estimateService.AddPartCatalogItemAsync(
            scenario.ManagerId,
            new AddPartCatalogItemToEstimateCommand
            {
                EstimateId = estimateId,
                PartCatalogItemId = partId,
                Quantity = 1,
            })).Value!;

        var originalDescription = (await writeScope.EstimateItems.SingleAsync(candidate => candidate.Id == itemId)).Description;

        await partService.UpdatePartAsync(
            scenario.ManagerId,
            new Application.Catalog.UpdatePartCatalogItemCommand
            {
                PartCatalogItemId = partId,
                Sku = "FLT-001",
                Name = "Premium Oil Filter",
                Description = "Changed description",
                DefaultUnitPrice = 99m,
                IsActive = true,
            });

        var item = await writeScope.EstimateItems.SingleAsync(candidate => candidate.Id == itemId);
        Assert.Equal(originalDescription, item.Description);
    }

    [Fact]
    public async Task EstimateCatalog_AddPart_DoesNotChangeInventoryBalance()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, clock);
        var partService = CatalogTestSupport.CreatePartCatalogService(writeScope, organizationContext);
        var inventoryService = CatalogTestSupport.CreateInventoryService(writeScope, organizationContext, clock);
        var estimateService = CatalogTestSupport.CreateEstimateService(writeScope, organizationContext, clock);
        var location = await CatalogTestSupport.CreateCatalogLocationScenarioAsync(writeScope, scenario.OrganizationId, suffix);
        var partId = await CatalogTestSupport.CreatePartAsync(partService, scenario.ManagerId, suffix);
        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            12m);
        var estimateId = await CatalogTestSupport.CreateDraftEstimateAsync(
            estimateService,
            writeScope,
            scenario.OrganizationId,
            scenario.ManagerId,
            suffix);

        var before = await CatalogTestSupport.GetTotalInventoryQuantityAsync(writeScope);
        var result = await estimateService.AddPartCatalogItemAsync(
            scenario.ManagerId,
            new AddPartCatalogItemToEstimateCommand
            {
                EstimateId = estimateId,
                PartCatalogItemId = partId,
                Quantity = 3,
            });
        var after = await CatalogTestSupport.GetTotalInventoryQuantityAsync(writeScope);

        Assert.True(result.Success);
        Assert.Equal(before, after);
        Assert.Equal(12m, after);
    }

    [Fact]
    public async Task EstimateLifecycle_Present_DoesNotChangeInventory()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, clock);
        var partService = CatalogTestSupport.CreatePartCatalogService(writeScope, organizationContext);
        var inventoryService = CatalogTestSupport.CreateInventoryService(writeScope, organizationContext, clock);
        var estimateService = CatalogTestSupport.CreateEstimateService(writeScope, organizationContext, clock);
        var location = await CatalogTestSupport.CreateCatalogLocationScenarioAsync(writeScope, scenario.OrganizationId, suffix);
        var partId = await CatalogTestSupport.CreatePartAsync(partService, scenario.ManagerId, suffix);
        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            8m);
        var estimateId = await CatalogTestSupport.CreateDraftEstimateAsync(
            estimateService,
            writeScope,
            scenario.OrganizationId,
            scenario.ManagerId,
            suffix);
        await estimateService.AddPartCatalogItemAsync(
            scenario.ManagerId,
            new AddPartCatalogItemToEstimateCommand
            {
                EstimateId = estimateId,
                PartCatalogItemId = partId,
                Quantity = 2,
            });

        var before = await CatalogTestSupport.GetTotalInventoryQuantityAsync(writeScope);
        await estimateService.PresentForApprovalAsync(scenario.ManagerId, estimateId);
        var after = await CatalogTestSupport.GetTotalInventoryQuantityAsync(writeScope);

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task EstimateLifecycle_Approval_DoesNotChangeInventory()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, clock);
        var partService = CatalogTestSupport.CreatePartCatalogService(writeScope, organizationContext);
        var inventoryService = CatalogTestSupport.CreateInventoryService(writeScope, organizationContext, clock);
        var estimateService = CatalogTestSupport.CreateEstimateService(writeScope, organizationContext, clock);
        var location = await CatalogTestSupport.CreateCatalogLocationScenarioAsync(writeScope, scenario.OrganizationId, suffix);
        var partId = await CatalogTestSupport.CreatePartAsync(partService, scenario.ManagerId, suffix);
        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            8m);
        var scenarioRo = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, scenario.OrganizationId, $"{suffix}-ro");
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(
            estimateService,
            scenario.ManagerId,
            scenarioRo.RepairOrder.Id);

        var before = await CatalogTestSupport.GetTotalInventoryQuantityAsync(writeScope);
        await estimateService.RecordCustomerApprovalAsync(scenario.ManagerId, estimateId);
        var after = await CatalogTestSupport.GetTotalInventoryQuantityAsync(writeScope);

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task CustomerPortal_Approval_DoesNotChangeInventory()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator(scenario.OrganizationId);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(scenario.OrganizationId), clock);
        var partService = CatalogTestSupport.CreatePartCatalogService(writeScope, new TestOrganizationContext(scenario.OrganizationId));
        var inventoryService = CatalogTestSupport.CreateInventoryService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            clock);
        var estimateService = CatalogTestSupport.CreateEstimateService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            clock);
        var location = await CatalogTestSupport.CreateCatalogLocationScenarioAsync(writeScope, scenario.OrganizationId, suffix);
        var partId = await CatalogTestSupport.CreatePartAsync(partService, scenario.ManagerId, suffix);
        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            6m);
        var scenarioRo = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, scenario.OrganizationId, $"{suffix}-ro");
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(
            estimateService,
            scenario.ManagerId,
            scenarioRo.RepairOrder.Id);
        var share = await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            clock,
            scenario.ManagerId,
            estimateId);
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);

        var before = await CatalogTestSupport.GetTotalInventoryQuantityAsync(writeScope);
        var result = await portalService.RecordApprovalAsync(share.PublicId);
        var after = await CatalogTestSupport.GetTotalInventoryQuantityAsync(writeScope);

        Assert.True(result.Success);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task RepairOrder_Create_DoesNotChangeInventory()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, clock);
        var partService = CatalogTestSupport.CreatePartCatalogService(writeScope, organizationContext);
        var inventoryService = CatalogTestSupport.CreateInventoryService(writeScope, organizationContext, clock);
        var repairOrderService = new RepairOrderManagementService(
            writeScope,
            organizationContext,
            new RepairOrderNumberGenerator(),
            clock);
        var location = await CatalogTestSupport.CreateCatalogLocationScenarioAsync(writeScope, scenario.OrganizationId, suffix);
        var partId = await CatalogTestSupport.CreatePartAsync(partService, scenario.ManagerId, suffix);
        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            5m);
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);

        var before = await CatalogTestSupport.GetTotalInventoryQuantityAsync(writeScope);
        var result = await repairOrderService.CreateRepairOrderAsync(new CreateRepairOrderCommand
        {
            WorkshopLocationId = location.Location.Id,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
        });
        var after = await CatalogTestSupport.GetTotalInventoryQuantityAsync(writeScope);

        Assert.True(result.Success);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task TechnicianWork_Start_DoesNotChangeInventory()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-technician");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            scenario.OrganizationId,
            technicianUser.Id,
            OrganizationMembershipRole.Technician);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, clock);
        var partService = CatalogTestSupport.CreatePartCatalogService(writeScope, organizationContext);
        var inventoryService = CatalogTestSupport.CreateInventoryService(writeScope, organizationContext, clock);
        var operationsService = new WorkshopOperationsService(writeScope, organizationContext, clock);
        var location = await CatalogTestSupport.CreateCatalogLocationScenarioAsync(writeScope, scenario.OrganizationId, suffix);
        var partId = await CatalogTestSupport.CreatePartAsync(partService, scenario.ManagerId, suffix);
        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            5m);
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, scenario.OrganizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, scenario.OrganizationId, suffix, customer.Id);
        var repairOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            scenario.OrganizationId,
            location.Location.Id,
            customer.Id,
            vehicle.Id,
            suffix,
            DefaultNow);
        var technician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            scenario.OrganizationId,
            location.Location.Id,
            suffix,
            technicianUser.Id);
        await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
            writeScope,
            scenario.OrganizationId,
            repairOrder.Id,
            technician.StaffMember.Id,
            DefaultNow);

        var before = await CatalogTestSupport.GetTotalInventoryQuantityAsync(writeScope);
        var result = await operationsService.StartAssignedWorkAsync(technicianUser.Id, repairOrder.Id);
        var after = await CatalogTestSupport.GetTotalInventoryQuantityAsync(writeScope);

        Assert.True(result.Success);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Inspection_Complete_DoesNotChangeInventory()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var organizationContext = new TestOrganizationContext(scenario.OrganizationId);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(organizationContext, clock);
        var partService = CatalogTestSupport.CreatePartCatalogService(writeScope, organizationContext);
        var inventoryService = CatalogTestSupport.CreateInventoryService(writeScope, organizationContext, clock);
        var inspectionService = new InspectionManagementService(writeScope, organizationContext, clock);
        var location = await CatalogTestSupport.CreateCatalogLocationScenarioAsync(writeScope, scenario.OrganizationId, suffix);
        var partId = await CatalogTestSupport.CreatePartAsync(partService, scenario.ManagerId, suffix);
        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            5m);
        var scenarioRo = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, scenario.OrganizationId, $"{suffix}-ro");
        var inspectionId = (await inspectionService.CreateInspectionAsync(scenario.ManagerId, scenarioRo.RepairOrder.Id)).Value!;
        await inspectionService.StartInspectionAsync(scenario.ManagerId, inspectionId);
        var items = await writeScope.InspectionItems.Where(candidate => candidate.InspectionId == inspectionId).ToListAsync();
        await inspectionService.UpdateInspectionItemsAsync(
            scenario.ManagerId,
            new UpdateInspectionItemsCommand
            {
                InspectionId = inspectionId,
                Items = items.Select(item => new InspectionItemUpdate
                {
                    InspectionItemId = item.Id,
                    Condition = InspectionCondition.Good,
                }).ToList(),
            });

        var before = await CatalogTestSupport.GetTotalInventoryQuantityAsync(writeScope);
        var result = await inspectionService.CompleteInspectionAsync(scenario.ManagerId, inspectionId);
        var after = await CatalogTestSupport.GetTotalInventoryQuantityAsync(writeScope);

        Assert.True(result.Success);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task CustomerPortalAuthentication_DoesNotAuthorizeCatalogRoutes()
    {
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var catalogService = CatalogTestSupport.CreateServiceCatalogService(scope.Context, new UnresolvedOrganizationContext());

        var listResult = await catalogService.ListServicesAsync(new Application.Catalog.ServiceCatalogListQuery());
        var createResult = await catalogService.CreateServiceAsync(
            Guid.CreateVersion7(),
            new Application.Catalog.CreateServiceCatalogItemCommand
            {
                Code = "PORTAL-BLOCKED",
                Name = "Portal blocked",
                DefaultUnitPrice = 1m,
            });

        Assert.Empty(listResult.Items);
        Assert.Equal(0, listResult.TotalCount);
        Assert.False(createResult.Success);
        Assert.Equal(Application.Catalog.CatalogOperationFailureReason.OrganizationUnresolved, createResult.FailureReason);
    }

    [Fact]
    public async Task CustomerPortalAuthentication_DoesNotAuthorizeInventoryRoutes()
    {
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var inventoryService = CatalogTestSupport.CreateInventoryService(
            scope.Context,
            new UnresolvedOrganizationContext(),
            new FakeTimeProvider(DefaultNow));

        var listResult = await inventoryService.ListInventoryAsync(new InventoryListQuery());
        var adjustResult = await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            PartInventoryMovementType.ManualIncrease,
            1m);

        Assert.Empty(listResult.Items);
        Assert.Equal(0, listResult.TotalCount);
        Assert.False(adjustResult.Success);
        Assert.Equal(InventoryAdjustmentFailureReason.OrganizationUnresolved, adjustResult.FailureReason);
    }

    [Fact]
    public async Task CustomerPortalProjection_DoesNotExposeInventoryData()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CatalogTestSupport.CreateCatalogScenarioAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator(scenario.OrganizationId);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(scenario.OrganizationId), clock);
        var partService = CatalogTestSupport.CreatePartCatalogService(writeScope, new TestOrganizationContext(scenario.OrganizationId));
        var inventoryService = CatalogTestSupport.CreateInventoryService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            clock);
        var estimateService = CatalogTestSupport.CreateEstimateService(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            clock);
        var location = await CatalogTestSupport.CreateCatalogLocationScenarioAsync(writeScope, scenario.OrganizationId, suffix);
        var partId = await CatalogTestSupport.CreatePartAsync(partService, scenario.ManagerId, suffix, sku: "INV-HIDDEN");
        await CatalogTestSupport.AdjustInventoryAsync(
            inventoryService,
            scenario.ManagerId,
            partId,
            location.Location.Id,
            PartInventoryMovementType.OpeningBalance,
            20m);
        var scenarioRo = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, scenario.OrganizationId, $"{suffix}-ro");
        var estimateId = (await estimateService.CreateEstimateAsync(scenario.ManagerId, scenarioRo.RepairOrder.Id)).Value!;
        await estimateService.AddPartCatalogItemAsync(
            scenario.ManagerId,
            new AddPartCatalogItemToEstimateCommand
            {
                EstimateId = estimateId,
                PartCatalogItemId = partId,
                Quantity = 2,
            });
        await estimateService.PresentForApprovalAsync(scenario.ManagerId, estimateId);
        var share = await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(scenario.OrganizationId),
            clock,
            scenario.ManagerId,
            estimateId);
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);

        var details = await portalService.GetSharedEstimateAsync(share.PublicId);
        var serialized = System.Text.Json.JsonSerializer.Serialize(details);

        Assert.NotNull(details);
        Assert.DoesNotContain("QuantityOnHand", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("PartInventory", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryBalance", serialized, StringComparison.Ordinal);
    }
}
