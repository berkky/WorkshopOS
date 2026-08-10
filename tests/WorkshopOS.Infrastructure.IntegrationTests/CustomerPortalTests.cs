using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.CustomerPortal;
using WorkshopOS.Application.Estimates;
using WorkshopOS.Application.EstimateSharing;
using WorkshopOS.Application.Inspections;
using WorkshopOS.Domain.Appointments;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Domain.Staff;
using WorkshopOS.Infrastructure.Inspections;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class CustomerPortalTests(PostgreSqlTestFixture fixture)
{
    private static readonly DateTimeOffset DefaultNow = EstimateShareTestSupport.DefaultNow;

    [Fact]
    public async Task CustomerPortal_ValidTokenExchangeSucceeds()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, _, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));

        var result = await portalService.ExchangeTokenAsync(new ExchangeEstimateShareTokenCommand
        {
            PublicId = share.PublicId,
            RawToken = share.RawToken,
        });

        Assert.True(result.Success);
    }

    [Fact]
    public async Task CustomerPortal_InvalidTokenFails()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, _, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));

        var result = await portalService.ExchangeTokenAsync(new ExchangeEstimateShareTokenCommand
        {
            PublicId = share.PublicId,
            RawToken = "invalid-token-value-that-is-not-the-secret",
        });

        Assert.False(result.Success);
        Assert.Equal(CustomerPortalOperationFailureReason.InvalidAccess, result.FailureReason);
    }

    [Fact]
    public async Task CustomerPortal_WrongTokenForPublicIdFails()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var scenarioA = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, $"{suffix}-a");
        var scenarioB = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, $"{suffix}-b");
        var estimateAId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, manager.ManagerId, scenarioA.RepairOrder.Id);
        var estimateBId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, manager.ManagerId, scenarioB.RepairOrder.Id);
        var shareA = await EstimateShareTestSupport.CreateActiveShareAsync(writeScope, new TestOrganizationContext(manager.OrganizationId), clock, manager.ManagerId, estimateAId);
        var shareB = await EstimateShareTestSupport.CreateActiveShareAsync(writeScope, new TestOrganizationContext(manager.OrganizationId), clock, manager.ManagerId, estimateBId);
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);

        var result = await portalService.ExchangeTokenAsync(new ExchangeEstimateShareTokenCommand
        {
            PublicId = shareA.PublicId,
            RawToken = shareB.RawToken,
        });

        Assert.False(result.Success);
        Assert.Equal(CustomerPortalOperationFailureReason.InvalidAccess, result.FailureReason);
    }

    [Fact]
    public async Task CustomerPortal_RevokedShareFails()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var sharingService = EstimateShareTestSupport.CreateSharingService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);
        await sharingService.RevokeShareAsync(manager.ManagerId, new RevokeEstimateShareCommand { EstimateId = estimateId });

        var result = await portalService.ExchangeTokenAsync(new ExchangeEstimateShareTokenCommand
        {
            PublicId = share.PublicId,
            RawToken = share.RawToken,
        });

        Assert.False(result.Success);
        Assert.Equal(CustomerPortalOperationFailureReason.InvalidAccess, result.FailureReason);
    }

    [Fact]
    public async Task CustomerPortal_ExpiredShareFails()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var createdAt = DefaultNow;
        var expiredAt = createdAt.AddDays(2);
        var clock = new FakeTimeProvider(createdAt);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, suffix);
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, manager.ManagerId, scenario.RepairOrder.Id);
        var share = await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock,
            manager.ManagerId,
            estimateId,
            durationDays: 1);

        var expiredClock = new FakeTimeProvider(expiredAt);
        var expiredPortalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, expiredClock);
        var result = await expiredPortalService.ExchangeTokenAsync(new ExchangeEstimateShareTokenCommand
        {
            PublicId = share.PublicId,
            RawToken = share.RawToken,
        });

        Assert.False(result.Success);
        Assert.Equal(CustomerPortalOperationFailureReason.InvalidAccess, result.FailureReason);
    }

    [Fact]
    public async Task CustomerPortal_PublicIdWithoutTokenDoesNotAuthorize()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, _, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));

        var exchangeResult = await portalService.ExchangeTokenAsync(new ExchangeEstimateShareTokenCommand
        {
            PublicId = share.PublicId,
            RawToken = string.Empty,
        });

        Assert.False(exchangeResult.Success);
        Assert.Equal(CustomerPortalOperationFailureReason.InvalidAccess, exchangeResult.FailureReason);
    }

    [Fact]
    public async Task CustomerPortal_TokenExchangeUpdatesLastAccessed()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, _, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);
        await portalService.ExchangeTokenAsync(new ExchangeEstimateShareTokenCommand
        {
            PublicId = share.PublicId,
            RawToken = share.RawToken,
        });

        var stored = await writeScope.EstimateShares.SingleAsync();
        Assert.Equal(DefaultNow, stored.LastAccessedAtUtc);
    }

    [Fact]
    public async Task CustomerPortal_ShowsSharedEstimateOnly()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var scenarioA = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, $"{suffix}-a");
        var scenarioB = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, $"{suffix}-b");
        var estimateAId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(
            estimateService,
            manager.ManagerId,
            scenarioA.RepairOrder.Id,
            "Shared line",
            150m);
        await EstimateShareTestSupport.CreatePresentedEstimateAsync(
            estimateService,
            manager.ManagerId,
            scenarioB.RepairOrder.Id,
            "Other line",
            999m);
        var share = await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock,
            manager.ManagerId,
            estimateAId);
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);

        var details = await portalService.GetSharedEstimateAsync(share.PublicId);

        Assert.NotNull(details);
        Assert.Single(details.Items);
        Assert.Equal("Shared line", details.Items[0].Description);
        Assert.Equal(150m, details.Total);
        Assert.NotEqual("Other line", details.Items[0].Description);
    }

    [Fact]
    public async Task CustomerPortal_ShowsEstimateItemsAndServerTotals()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, suffix);
        var estimateId = (await estimateService.CreateEstimateAsync(manager.ManagerId, scenario.RepairOrder.Id)).Value!;
        await EstimateShareTestSupport.AddEstimateItemAsync(estimateService, manager.ManagerId, estimateId, "First line", 1, 100m);
        await EstimateShareTestSupport.AddEstimateItemAsync(estimateService, manager.ManagerId, estimateId, "Second line", 2, 10.125m);
        await estimateService.PresentForApprovalAsync(manager.ManagerId, estimateId);
        var share = await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock,
            manager.ManagerId,
            estimateId);
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);

        var details = await portalService.GetSharedEstimateAsync(share.PublicId);

        Assert.NotNull(details);
        Assert.Equal(2, details.Items.Count);
        Assert.Equal(120.26m, details.Total);
        Assert.All(details.Items, item =>
            Assert.Equal(EstimateMoneyCalculator.CalculateLineTotal(item.Quantity, item.UnitPrice), item.LineTotal));
    }

    [Fact]
    public async Task CustomerPortal_UsesHistoricalRepairOrderCustomerContext()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, suffix);
        var replacementCustomer = await TestDataFactory.PersistCustomerAsync(writeScope, manager.OrganizationId, $"{suffix}-replacement");
        writeScope.Entry(scenario.Vehicle).Property<Guid?>(nameof(Domain.Vehicles.Vehicle.CurrentCustomerId)).CurrentValue = replacementCustomer.Id;
        await writeScope.SaveChangesAsync();

        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, manager.ManagerId, scenario.RepairOrder.Id);
        var share = await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock,
            manager.ManagerId,
            estimateId);
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);

        var details = await portalService.GetSharedEstimateAsync(share.PublicId);

        Assert.NotNull(details);
        Assert.Equal(scenario.RepairOrder.Number, details.RepairOrderNumber);
        Assert.Equal(scenario.Vehicle.Make, details.VehicleMake);
        Assert.Equal(scenario.Vehicle.Model, details.VehicleModel);
    }

    [Fact]
    public async Task CustomerPortal_DoesNotExposeInternalRepairOrderNotes()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        const string internalNotes = "SECRET-RO-NOTES";
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(
            writeScope,
            manager.OrganizationId,
            suffix,
            internalNotes: internalNotes);
        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, manager.ManagerId, scenario.RepairOrder.Id);
        var share = await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock,
            manager.ManagerId,
            estimateId);
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);

        var details = await portalService.GetSharedEstimateAsync(share.PublicId);
        var serialized = System.Text.Json.JsonSerializer.Serialize(details);

        Assert.NotNull(details);
        Assert.DoesNotContain(internalNotes, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(RepairOrder.InternalNotes), serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomerPortal_DoesNotExposeInspectionTechnicianNotes()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        const string technicianNotes = "SECRET-TECH-NOTES";
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, suffix);
        var inspection = new Inspection(manager.OrganizationId, scenario.RepairOrder.Id, technicianNotes: technicianNotes);
        writeScope.Inspections.Add(inspection);
        await writeScope.SaveChangesAsync();

        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, manager.ManagerId, scenario.RepairOrder.Id);
        var share = await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock,
            manager.ManagerId,
            estimateId);
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);

        var details = await portalService.GetSharedEstimateAsync(share.PublicId);
        var serialized = System.Text.Json.JsonSerializer.Serialize(details);

        Assert.NotNull(details);
        Assert.DoesNotContain(technicianNotes, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomerPortal_DoesNotExposeInspectionItemNotes()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        const string itemNotes = "SECRET-ITEM-NOTES";
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, suffix);
        var inspectionService = new InspectionManagementService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var inspectionId = (await inspectionService.CreateInspectionAsync(manager.ManagerId, scenario.RepairOrder.Id)).Value!;
        await inspectionService.StartInspectionAsync(manager.ManagerId, inspectionId);
        var inspectionItem = await writeScope.InspectionItems.FirstAsync(candidate => candidate.InspectionId == inspectionId);
        await inspectionService.UpdateInspectionItemsAsync(
            manager.ManagerId,
            new UpdateInspectionItemsCommand
            {
                InspectionId = inspectionId,
                Items =
                [
                    new InspectionItemUpdate
                    {
                        InspectionItemId = inspectionItem.Id,
                        Condition = InspectionCondition.Critical,
                        Notes = itemNotes,
                    },
                ],
            });

        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, manager.ManagerId, scenario.RepairOrder.Id);
        var share = await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock,
            manager.ManagerId,
            estimateId);
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);

        var details = await portalService.GetSharedEstimateAsync(share.PublicId);
        var serialized = System.Text.Json.JsonSerializer.Serialize(details);

        Assert.NotNull(details);
        Assert.DoesNotContain(itemNotes, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomerPortal_DoesNotExposeInspectionMedia()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, suffix);
        var inspection = new Inspection(manager.OrganizationId, scenario.RepairOrder.Id);
        writeScope.Inspections.Add(inspection);
        await writeScope.SaveChangesAsync();
        var inspectionItem = new InspectionItem(manager.OrganizationId, inspection.Id, "Brakes", "Pads", InspectionCondition.Critical, 10);
        writeScope.InspectionItems.Add(inspectionItem);
        await writeScope.SaveChangesAsync();
        writeScope.InspectionMediaAssets.Add(new InspectionMediaAsset(
            manager.OrganizationId,
            inspection.Id,
            inspectionItem.Id,
            $"{Guid.CreateVersion7():N}.jpg",
            InspectionMediaKind.Photo,
            "image/jpeg",
            16,
            new string('a', 64),
            manager.ManagerId,
            DefaultNow));
        await writeScope.SaveChangesAsync();

        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, manager.ManagerId, scenario.RepairOrder.Id);
        var share = await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock,
            manager.ManagerId,
            estimateId);
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);

        var details = await portalService.GetSharedEstimateAsync(share.PublicId);
        var serialized = System.Text.Json.JsonSerializer.Serialize(details);

        Assert.NotNull(details);
        Assert.DoesNotContain("InspectionMedia", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("StorageKey", serialized, StringComparison.Ordinal);
        Assert.Equal(1, await writeScope.InspectionMediaAssets.CountAsync());
    }

    [Fact]
    public async Task CustomerPortal_DoesNotExposeStaffOrMembershipData()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, suffix);
        await TestDataFactory.PersistTechnicianAtLocationAsync(writeScope, manager.OrganizationId, scenario.Location.Id, suffix, manager.ManagerId);
        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, manager.ManagerId, scenario.RepairOrder.Id);
        var share = await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock,
            manager.ManagerId,
            estimateId);
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);

        var details = await portalService.GetSharedEstimateAsync(share.PublicId);
        var serialized = System.Text.Json.JsonSerializer.Serialize(details);

        Assert.NotNull(details);
        Assert.DoesNotContain("StaffMember", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("OrganizationMembership", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(manager.ManagerId.ToString(), serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CustomerPortal_SentEstimateCanBeApproved()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, _, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));

        var result = await portalService.RecordApprovalAsync(share.PublicId);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task CustomerPortal_ApprovalMarksEstimateApproved()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));
        await portalService.RecordApprovalAsync(share.PublicId);

        var estimate = await writeScope.Estimates.SingleAsync(candidate => candidate.Id == estimateId);
        Assert.Equal(EstimateStatus.Approved, estimate.Status);
    }

    [Fact]
    public async Task CustomerPortal_ApprovalRecordsShareDecisionApproved()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, _, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));
        await portalService.RecordApprovalAsync(share.PublicId);

        var storedShare = await writeScope.EstimateShares.SingleAsync();
        Assert.Equal(EstimateShareDecision.Approved, storedShare.Decision);
    }

    [Fact]
    public async Task CustomerPortal_ApprovalRecordsDecisionTimestamp()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, _, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));
        await portalService.RecordApprovalAsync(share.PublicId);

        var storedShare = await writeScope.EstimateShares.SingleAsync();
        Assert.Equal(DefaultNow, storedShare.DecisionAtUtc);
    }

    [Fact]
    public async Task CustomerPortal_ApprovedEstimateBecomesReadOnly()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, _, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));
        await portalService.RecordApprovalAsync(share.PublicId);

        var details = await portalService.GetSharedEstimateAsync(share.PublicId);
        Assert.NotNull(details);
        Assert.False(details.CanApprove);
        Assert.False(details.CanDecline);
    }

    [Fact]
    public async Task CustomerPortal_ApprovalDoesNotRequireStaffMembership()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, _, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));

        var result = await portalService.RecordApprovalAsync(share.PublicId);
        Assert.True(result.Success);
        Assert.Equal(0, await writeScope.OrganizationMemberships.CountAsync(candidate => candidate.UserId == Guid.Empty));
    }

    [Fact]
    public async Task CustomerPortal_ApprovalRequiresValidPortalShareSession()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var sharingService = EstimateShareTestSupport.CreateSharingService(writeScope, new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));
        await sharingService.RevokeShareAsync(manager.ManagerId, new RevokeEstimateShareCommand { EstimateId = estimateId });

        var validation = await portalService.ValidateSessionAsync(share.PublicId);
        var result = await portalService.RecordApprovalAsync(share.PublicId);

        Assert.Null(validation);
        Assert.False(result.Success);
        Assert.Equal(CustomerPortalOperationFailureReason.InvalidAccess, result.FailureReason);
    }

    [Fact]
    public async Task CustomerPortal_SentEstimateCanBeDeclined()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, _, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));

        var result = await portalService.RecordDeclineAsync(share.PublicId);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task CustomerPortal_DeclineMarksEstimateDeclined()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));
        await portalService.RecordDeclineAsync(share.PublicId);

        var estimate = await writeScope.Estimates.SingleAsync(candidate => candidate.Id == estimateId);
        Assert.Equal(EstimateStatus.Declined, estimate.Status);
    }

    [Fact]
    public async Task CustomerPortal_DeclineRecordsShareDecisionDeclined()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, _, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));
        await portalService.RecordDeclineAsync(share.PublicId);

        var storedShare = await writeScope.EstimateShares.SingleAsync();
        Assert.Equal(EstimateShareDecision.Declined, storedShare.Decision);
    }

    [Fact]
    public async Task CustomerPortal_DeclineRecordsDecisionTimestamp()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, _, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));
        await portalService.RecordDeclineAsync(share.PublicId);

        var storedShare = await writeScope.EstimateShares.SingleAsync();
        Assert.Equal(DefaultNow, storedShare.DecisionAtUtc);
    }

    [Fact]
    public async Task CustomerPortal_DeclinedEstimateBecomesReadOnly()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, _, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));
        await portalService.RecordDeclineAsync(share.PublicId);

        var details = await portalService.GetSharedEstimateAsync(share.PublicId);
        Assert.NotNull(details);
        Assert.False(details.CanApprove);
        Assert.False(details.CanDecline);
    }

    [Fact]
    public async Task CustomerPortal_DeclineRequiresValidPortalShareSession()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var sharingService = EstimateShareTestSupport.CreateSharingService(writeScope, new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));
        await sharingService.RevokeShareAsync(manager.ManagerId, new RevokeEstimateShareCommand { EstimateId = estimateId });

        var validation = await portalService.ValidateSessionAsync(share.PublicId);
        var result = await portalService.RecordDeclineAsync(share.PublicId);

        Assert.Null(validation);
        Assert.False(result.Success);
        Assert.Equal(CustomerPortalOperationFailureReason.InvalidAccess, result.FailureReason);
    }

    [Fact]
    public async Task CustomerPortal_ApprovedEstimateCannotBeDeclined()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, _, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));
        await portalService.RecordApprovalAsync(share.PublicId);

        var result = await portalService.RecordDeclineAsync(share.PublicId);
        Assert.False(result.Success);
        Assert.Equal(CustomerPortalOperationFailureReason.InvalidLifecycleTransition, result.FailureReason);
    }

    [Fact]
    public async Task CustomerPortal_DeclinedEstimateCannotBeApproved()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, _, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));
        await portalService.RecordDeclineAsync(share.PublicId);

        var result = await portalService.RecordApprovalAsync(share.PublicId);
        Assert.False(result.Success);
        Assert.Equal(CustomerPortalOperationFailureReason.InvalidLifecycleTransition, result.FailureReason);
    }

    [Fact]
    public async Task CustomerPortal_DecisionCannotBeSubmittedTwice()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, _, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));
        await portalService.RecordApprovalAsync(share.PublicId);

        var secondResult = await portalService.RecordApprovalAsync(share.PublicId);
        Assert.False(secondResult.Success);
        Assert.Equal(CustomerPortalOperationFailureReason.InvalidLifecycleTransition, secondResult.FailureReason);
    }

    [Fact]
    public async Task CustomerPortal_StaffDecisionThenPortalDecisionIsRejected()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));
        await estimateService.RecordCustomerApprovalAsync(manager.ManagerId, estimateId);

        var result = await portalService.RecordDeclineAsync(share.PublicId);
        Assert.False(result.Success);
        Assert.Equal(CustomerPortalOperationFailureReason.InvalidLifecycleTransition, result.FailureReason);
    }

    [Fact]
    public async Task CustomerPortal_PortalDecisionThenStaffDecisionIsRejected()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));
        await portalService.RecordApprovalAsync(share.PublicId);

        var result = await estimateService.RecordCustomerDeclineAsync(manager.ManagerId, estimateId);
        Assert.False(result.Success);
        Assert.Equal(EstimateOperationFailureReason.InvalidLifecycleTransition, result.FailureReason);
    }

    [Fact]
    public async Task CustomerPortal_RevokedShareCannotApprove()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var sharingService = EstimateShareTestSupport.CreateSharingService(writeScope, new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));
        await sharingService.RevokeShareAsync(manager.ManagerId, new RevokeEstimateShareCommand { EstimateId = estimateId });

        var result = await portalService.RecordApprovalAsync(share.PublicId);
        Assert.False(result.Success);
        Assert.Equal(CustomerPortalOperationFailureReason.InvalidAccess, result.FailureReason);
    }

    [Fact]
    public async Task CustomerPortal_ExpiredShareCannotApprove()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var createdAt = DefaultNow;
        var expiredAt = createdAt.AddDays(2);
        var clock = new FakeTimeProvider(createdAt);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, suffix);
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, manager.ManagerId, scenario.RepairOrder.Id);
        var share = await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock,
            manager.ManagerId,
            estimateId,
            durationDays: 1);
        var expiredPortalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(expiredAt));

        var result = await expiredPortalService.RecordApprovalAsync(share.PublicId);
        Assert.False(result.Success);
        Assert.Equal(CustomerPortalOperationFailureReason.InvalidAccess, result.FailureReason);
    }

    [Fact]
    public async Task CustomerPortal_RevokedShareCannotDecline()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var sharingService = EstimateShareTestSupport.CreateSharingService(writeScope, new TestOrganizationContext(manager.OrganizationId), new FakeTimeProvider(DefaultNow));
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(DefaultNow));
        await sharingService.RevokeShareAsync(manager.ManagerId, new RevokeEstimateShareCommand { EstimateId = estimateId });

        var result = await portalService.RecordDeclineAsync(share.PublicId);
        Assert.False(result.Success);
        Assert.Equal(CustomerPortalOperationFailureReason.InvalidAccess, result.FailureReason);
    }

    [Fact]
    public async Task CustomerPortal_ExpiredShareCannotDecline()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var createdAt = DefaultNow;
        var expiredAt = createdAt.AddDays(2);
        var clock = new FakeTimeProvider(createdAt);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, suffix);
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, manager.ManagerId, scenario.RepairOrder.Id);
        var share = await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock,
            manager.ManagerId,
            estimateId,
            durationDays: 1);
        var expiredPortalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, new FakeTimeProvider(expiredAt));

        var result = await expiredPortalService.RecordDeclineAsync(share.PublicId);
        Assert.False(result.Success);
        Assert.Equal(CustomerPortalOperationFailureReason.InvalidAccess, result.FailureReason);
    }

    [Fact]
    public async Task StaffAuthentication_DoesNotAuthorizeCustomerPortalRoute()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, suffix);
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, manager.ManagerId, scenario.RepairOrder.Id);
        var share = await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock,
            manager.ManagerId,
            estimateId);
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);

        var result = await portalService.RecordApprovalAsync(share.PublicId);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task CustomerPortalAuthentication_DoesNotAuthorizeOrganizationMemberRoute()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var owner = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner");
        var technician = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-technician");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, owner.Id, OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            technician.Id,
            OrganizationMembershipRole.Technician);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), clock);
        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(organization.Id), clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, owner.Id, scenario.RepairOrder.Id);
        var sharingService = EstimateShareTestSupport.CreateSharingService(writeScope, new TestOrganizationContext(organization.Id), clock);

        var result = await sharingService.CreateShareAsync(
            technician.Id,
            new CreateEstimateShareCommand { EstimateId = estimateId });

        Assert.False(result.Success);
        Assert.Equal(EstimateShareOperationFailureReason.Unauthorized, result.FailureReason);
    }

    [Fact]
    public async Task CustomerPortalAuthentication_DoesNotAuthorizeEstimateManagerRoute()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var owner = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner");
        var viewer = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-viewer");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, owner.Id, OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            viewer.Id,
            OrganizationMembershipRole.Viewer);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), clock);
        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(organization.Id), clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, owner.Id, scenario.RepairOrder.Id);
        await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(organization.Id),
            clock,
            owner.Id,
            estimateId);
        var sharingService = EstimateShareTestSupport.CreateSharingService(writeScope, new TestOrganizationContext(organization.Id), clock);

        var result = await sharingService.RevokeShareAsync(
            viewer.Id,
            new RevokeEstimateShareCommand { EstimateId = estimateId });

        Assert.False(result.Success);
        Assert.Equal(EstimateShareOperationFailureReason.Unauthorized, result.FailureReason);
    }

    [Fact]
    public void CustomerPortalCookie_ContainsNoRawShareToken()
    {
        var validation = new CustomerPortalSessionValidation
        {
            SharePublicId = Guid.CreateVersion7(),
            OrganizationId = Guid.CreateVersion7(),
            EstimateId = Guid.CreateVersion7(),
        };

        var serialized = System.Text.Json.JsonSerializer.Serialize(validation);
        Assert.DoesNotContain("RawToken", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("token", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CustomerPortal_PortalSessionIsScopedToExactShare()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var scenarioA = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, $"{suffix}-a");
        var scenarioB = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, $"{suffix}-b");
        var estimateAId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, manager.ManagerId, scenarioA.RepairOrder.Id, "Share A", 100m);
        await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, manager.ManagerId, scenarioB.RepairOrder.Id, "Share B", 200m);
        var shareA = await EstimateShareTestSupport.CreateActiveShareAsync(writeScope, new TestOrganizationContext(manager.OrganizationId), clock, manager.ManagerId, estimateAId);
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);

        var wrongSession = await portalService.ValidateSessionAsync(Guid.CreateVersion7());
        var details = await portalService.GetSharedEstimateAsync(shareA.PublicId);

        Assert.Null(wrongSession);
        Assert.NotNull(details);
        Assert.Equal("Share A", details.Items[0].Description);
    }

    [Fact]
    public async Task CustomerPortal_Approval_DoesNotCompleteRepairOrder()
    {
        await AssertDecisionDoesNotMutateWorkflowAsync(
            portalService => portalService.RecordApprovalAsync,
            assertRepairOrderStatus: RepairOrderStatus.InProgress);
    }

    [Fact]
    public async Task CustomerPortal_Decline_DoesNotCancelRepairOrder()
    {
        await AssertDecisionDoesNotMutateWorkflowAsync(
            portalService => portalService.RecordDeclineAsync,
            assertRepairOrderStatus: RepairOrderStatus.InProgress);
    }

    [Fact]
    public async Task CustomerPortal_Decision_DoesNotChangeRepairOrderPriority()
    {
        await AssertDecisionDoesNotMutateWorkflowAsync(
            portalService => portalService.RecordApprovalAsync,
            assertPriority: RepairOrderPriority.High);
    }

    [Fact]
    public async Task CustomerPortal_Decision_DoesNotChangeTechnicianWorkStatus()
    {
        await AssertDecisionDoesNotMutateWorkflowAsync(
            portalService => portalService.RecordApprovalAsync,
            assertTechnicianWorkStatus: TechnicianWorkStatus.InProgress);
    }

    [Fact]
    public async Task CustomerPortal_Decision_DoesNotChangeAppointmentStatus()
    {
        await AssertDecisionDoesNotMutateWorkflowAsync(
            portalService => portalService.RecordApprovalAsync,
            assertAppointmentStatus: AppointmentStatus.Scheduled);
    }

    [Fact]
    public async Task CustomerPortal_Decision_DoesNotChangeInspectionStatus()
    {
        await AssertDecisionDoesNotMutateWorkflowAsync(
            portalService => portalService.RecordApprovalAsync,
            assertInspectionStatus: InspectionStatus.InProgress);
    }

    [Fact]
    public async Task CustomerPortal_Decision_DoesNotChangeInspectionItemCondition()
    {
        await AssertDecisionDoesNotMutateWorkflowAsync(
            portalService => portalService.RecordApprovalAsync,
            assertInspectionCondition: InspectionCondition.Critical,
            assertInspectionItemNotes: "Worn pads");
    }

    [Fact]
    public async Task CustomerPortal_Decision_DoesNotChangeInspectionMedia()
    {
        await AssertDecisionDoesNotMutateWorkflowAsync(
            portalService => portalService.RecordDeclineAsync,
            includeMedia: true);
    }

    private async Task AssertDecisionDoesNotMutateWorkflowAsync(
        Func<ICustomerEstimatePortalService, Func<Guid, CancellationToken, Task<CustomerPortalOperationResult>>> decisionSelector,
        RepairOrderStatus? assertRepairOrderStatus = null,
        RepairOrderPriority? assertPriority = null,
        TechnicianWorkStatus? assertTechnicianWorkStatus = null,
        AppointmentStatus? assertAppointmentStatus = null,
        InspectionStatus? assertInspectionStatus = null,
        InspectionCondition? assertInspectionCondition = null,
        string? assertInspectionItemNotes = null,
        bool includeMedia = false)
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator();
        var appointmentStart = DefaultNow.AddHours(-2);
        var appointmentEnd = appointmentStart.AddHours(1);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(
            writeScope,
            manager.OrganizationId,
            suffix,
            RepairOrderStatus.InProgress,
            assertPriority ?? RepairOrderPriority.High);
        var appointment = await TestDataFactory.PersistAppointmentAsync(
            writeScope,
            manager.OrganizationId,
            scenario.Location.Id,
            scenario.Customer.Id,
            scenario.Vehicle.Id,
            appointmentStart,
            appointmentEnd,
            assertAppointmentStatus ?? AppointmentStatus.Scheduled);
        writeScope.Entry(scenario.RepairOrder).Property<Guid?>(nameof(RepairOrder.AppointmentId)).CurrentValue = appointment.Id;
        await writeScope.SaveChangesAsync();

        var technician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            manager.OrganizationId,
            scenario.Location.Id,
            suffix,
            manager.ManagerId);
        var assignment = await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
            writeScope,
            manager.OrganizationId,
            scenario.RepairOrder.Id,
            technician.StaffMember.Id,
            DefaultNow);
        if (assertTechnicianWorkStatus == TechnicianWorkStatus.InProgress)
        {
            assignment.StartWork(DefaultNow);
            await writeScope.SaveChangesAsync();
        }

        var inspectionService = new InspectionManagementService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var inspectionId = (await inspectionService.CreateInspectionAsync(manager.ManagerId, scenario.RepairOrder.Id)).Value!;
        await inspectionService.StartInspectionAsync(manager.ManagerId, inspectionId);
        var inspectionItem = await writeScope.InspectionItems.FirstAsync(candidate => candidate.InspectionId == inspectionId);
        await inspectionService.UpdateInspectionItemsAsync(
            manager.ManagerId,
            new UpdateInspectionItemsCommand
            {
                InspectionId = inspectionId,
                Items =
                [
                    new InspectionItemUpdate
                    {
                        InspectionItemId = inspectionItem.Id,
                        Condition = assertInspectionCondition ?? InspectionCondition.Critical,
                        Notes = assertInspectionItemNotes ?? "Worn pads",
                    },
                ],
            });

        Guid? mediaId = null;
        if (includeMedia)
        {
            var media = new InspectionMediaAsset(
                manager.OrganizationId,
                inspectionId,
                inspectionItem.Id,
                $"{Guid.CreateVersion7():N}.jpg",
                InspectionMediaKind.Photo,
                "image/jpeg",
                16,
                new string('b', 64),
                manager.ManagerId,
                DefaultNow);
            writeScope.InspectionMediaAssets.Add(media);
            await writeScope.SaveChangesAsync();
            mediaId = media.Id;
        }

        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, manager.ManagerId, scenario.RepairOrder.Id);
        var share = await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock,
            manager.ManagerId,
            estimateId);
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);

        var beforeRepairOrder = await writeScope.RepairOrders.AsNoTracking().SingleAsync(candidate => candidate.Id == scenario.RepairOrder.Id);
        var beforeAppointment = await writeScope.Appointments.AsNoTracking().SingleAsync(candidate => candidate.Id == appointment.Id);
        var beforeAssignment = await writeScope.RepairOrderTechnicianAssignments.AsNoTracking().SingleAsync(candidate => candidate.Id == assignment.Id);
        var beforeInspection = await writeScope.Inspections.AsNoTracking().SingleAsync(candidate => candidate.Id == inspectionId);
        var beforeInspectionItem = await writeScope.InspectionItems.AsNoTracking().SingleAsync(candidate => candidate.Id == inspectionItem.Id);
        InspectionMediaAsset? beforeMedia = null;
        if (mediaId.HasValue)
        {
            beforeMedia = await writeScope.InspectionMediaAssets.AsNoTracking().SingleAsync(candidate => candidate.Id == mediaId.Value);
        }

        var decision = decisionSelector(portalService);
        var result = await decision(share.PublicId, CancellationToken.None);
        Assert.True(result.Success);

        var afterRepairOrder = await writeScope.RepairOrders.AsNoTracking().SingleAsync(candidate => candidate.Id == scenario.RepairOrder.Id);
        var afterAppointment = await writeScope.Appointments.AsNoTracking().SingleAsync(candidate => candidate.Id == appointment.Id);
        var afterAssignment = await writeScope.RepairOrderTechnicianAssignments.AsNoTracking().SingleAsync(candidate => candidate.Id == assignment.Id);
        var afterInspection = await writeScope.Inspections.AsNoTracking().SingleAsync(candidate => candidate.Id == inspectionId);
        var afterInspectionItem = await writeScope.InspectionItems.AsNoTracking().SingleAsync(candidate => candidate.Id == inspectionItem.Id);

        Assert.Equal(beforeRepairOrder.Status, afterRepairOrder.Status);
        Assert.Equal(assertRepairOrderStatus ?? beforeRepairOrder.Status, afterRepairOrder.Status);
        Assert.Equal(beforeRepairOrder.Priority, afterRepairOrder.Priority);
        Assert.Equal(assertPriority ?? beforeRepairOrder.Priority, afterRepairOrder.Priority);
        Assert.Equal(beforeAppointment.Status, afterAppointment.Status);
        Assert.Equal(assertAppointmentStatus ?? beforeAppointment.Status, afterAppointment.Status);
        Assert.Equal(beforeAssignment.WorkStatus, afterAssignment.WorkStatus);
        Assert.Equal(assertTechnicianWorkStatus ?? beforeAssignment.WorkStatus, afterAssignment.WorkStatus);
        Assert.Equal(beforeInspection.Status, afterInspection.Status);
        Assert.Equal(assertInspectionStatus ?? beforeInspection.Status, afterInspection.Status);
        Assert.Equal(beforeInspectionItem.Condition, afterInspectionItem.Condition);
        Assert.Equal(assertInspectionCondition ?? beforeInspectionItem.Condition, afterInspectionItem.Condition);
        Assert.Equal(beforeInspectionItem.Notes, afterInspectionItem.Notes);

        if (mediaId.HasValue && beforeMedia is not null)
        {
            var afterMedia = await writeScope.InspectionMediaAssets.AsNoTracking().SingleAsync(candidate => candidate.Id == mediaId.Value);
            Assert.Equal(beforeMedia.StorageKey, afterMedia.StorageKey);
            Assert.Equal(beforeMedia.Sha256, afterMedia.Sha256);
            Assert.Equal(beforeMedia.ContentType, afterMedia.ContentType);
        }
    }
}
