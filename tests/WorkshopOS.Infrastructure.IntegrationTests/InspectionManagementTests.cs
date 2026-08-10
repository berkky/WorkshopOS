using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Inspections;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Domain.Staff;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Infrastructure.Inspections;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class InspectionManagementTests(PostgreSqlTestFixture fixture)
{
    private static readonly DateTimeOffset DefaultNow = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InspectionManagement_Create_UsesCurrentOrganization()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            manager.Id,
            OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id);

        Assert.True(result.Success);
        var inspection = await writeScope.Inspections.SingleAsync(candidate => candidate.Id == result.Value);
        Assert.Equal(organization.Id, inspection.OrganizationId);
        Assert.Equal(scenario.RepairOrder.Id, inspection.RepairOrderId);
        Assert.Equal(InspectionStatus.Draft, inspection.Status);
    }

    [Fact]
    public async Task InspectionManagement_Create_WhenOrganizationUnresolved_FailsClosed()
    {
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var service = CreateService(scope.Context, new UnresolvedOrganizationContext(), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateInspectionAsync(Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.False(result.Success);
        Assert.Equal(InspectionOperationFailureReason.OrganizationUnresolved, result.FailureReason);
        Assert.Equal(0, await scope.Context.Inspections.CountAsync());
    }

    [Fact]
    public async Task InspectionManagement_Create_RejectsOtherTenantRepairOrder()
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

        var result = await service.CreateInspectionAsync(manager.Id, repairOrderBId);

        Assert.False(result.Success);
        Assert.Equal(InspectionOperationFailureReason.RepairOrderNotFound, result.FailureReason);
        Assert.Equal(0, await writeScopeA.Inspections.CountAsync());
    }

    [Fact]
    public async Task InspectionManagement_Create_RejectsCancelledRepairOrder()
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

        var result = await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id);

        Assert.False(result.Success);
        Assert.Equal(InspectionOperationFailureReason.RepairOrderNotEligible, result.FailureReason);
    }

    [Fact]
    public async Task InspectionManagement_Create_RejectsCompletedRepairOrder()
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

        var result = await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id);

        Assert.False(result.Success);
        Assert.Equal(InspectionOperationFailureReason.RepairOrderNotEligible, result.FailureReason);
    }

    [Fact]
    public async Task InspectionManagement_Create_CreatesDefaultStructuredItems()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id);

        Assert.True(result.Success);
        var items = await writeScope.InspectionItems
            .Where(candidate => candidate.InspectionId == result.Value)
            .OrderBy(candidate => candidate.SortOrder)
            .ToListAsync();
        Assert.Equal(DefaultVehicleInspectionTemplate.Items.Count, items.Count);
        Assert.All(items, item => Assert.Equal(InspectionCondition.NotChecked, item.Condition));
    }

    [Fact]
    public async Task InspectionManagement_Create_DefaultItemsAreServerControlled()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id);
        var items = await writeScope.InspectionItems
            .Where(candidate => candidate.InspectionId == result.Value)
            .OrderBy(candidate => candidate.SortOrder)
            .ToListAsync();

        for (var index = 0; index < DefaultVehicleInspectionTemplate.Items.Count; index++)
        {
            var template = DefaultVehicleInspectionTemplate.Items[index];
            Assert.Equal(template.Section, items[index].Section);
            Assert.Equal(template.Name, items[index].Name);
            Assert.Equal(template.SortOrder, items[index].SortOrder);
        }
    }

    [Fact]
    public async Task InspectionManagement_Create_IsAtomic()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id));
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));

        var result = await service.CreateInspectionAsync(manager.Id, Guid.CreateVersion7());

        Assert.False(result.Success);
        Assert.Equal(0, await writeScope.Inspections.CountAsync());
        Assert.Equal(0, await writeScope.InspectionItems.CountAsync());
    }

    [Fact]
    public async Task InspectionLifecycle_Start_DraftToInProgress()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = (await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id)).Value!;

        var result = await service.StartInspectionAsync(manager.Id, inspectionId);

        Assert.True(result.Success);
        var inspection = await writeScope.Inspections.SingleAsync(candidate => candidate.Id == inspectionId);
        Assert.Equal(InspectionStatus.InProgress, inspection.Status);
        Assert.NotNull(inspection.StartedAtUtc);
    }

    [Fact]
    public async Task InspectionLifecycle_CannotStartCompletedInspection()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = await CreateCompletedInspectionAsync(service, manager.Id, scenario.RepairOrder.Id, writeScope);

        var result = await service.StartInspectionAsync(manager.Id, inspectionId);

        Assert.False(result.Success);
        Assert.Equal(InspectionOperationFailureReason.InvalidLifecycleTransition, result.FailureReason);
    }

    [Fact]
    public async Task InspectionLifecycle_ItemUpdateRequiresInProgress()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = (await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id)).Value!;
        var item = await writeScope.InspectionItems.FirstAsync(candidate => candidate.InspectionId == inspectionId);

        var result = await service.UpdateInspectionItemsAsync(
            manager.Id,
            new UpdateInspectionItemsCommand
            {
                InspectionId = inspectionId,
                Items = [new InspectionItemUpdate
                {
                    InspectionItemId = item.Id,
                    Condition = InspectionCondition.Good,
                }],
            });

        Assert.False(result.Success);
        Assert.Equal(InspectionOperationFailureReason.InvalidLifecycleTransition, result.FailureReason);
    }

    [Fact]
    public async Task InspectionLifecycle_CompleteRequiresAllItemsInspected()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = (await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id)).Value!;
        await service.StartInspectionAsync(manager.Id, inspectionId);

        var result = await service.CompleteInspectionAsync(manager.Id, inspectionId);

        Assert.False(result.Success);
        Assert.Equal(InspectionOperationFailureReason.ItemsNotFullyInspected, result.FailureReason);
    }

    [Fact]
    public async Task InspectionLifecycle_EmptyInspectionCannotComplete()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = (await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id)).Value!;
        await service.StartInspectionAsync(manager.Id, inspectionId);

        var items = await writeScope.InspectionItems.Where(candidate => candidate.InspectionId == inspectionId).ToListAsync();
        writeScope.InspectionItems.RemoveRange(items);
        await writeScope.SaveChangesAsync();

        var result = await service.CompleteInspectionAsync(manager.Id, inspectionId);

        Assert.False(result.Success);
        Assert.Equal(InspectionOperationFailureReason.EmptyInspection, result.FailureReason);
    }

    [Fact]
    public async Task InspectionLifecycle_Complete_MarksInspectionCompleted()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = await CreateCompletedInspectionAsync(service, manager.Id, scenario.RepairOrder.Id, writeScope);

        var inspection = await writeScope.Inspections.SingleAsync(candidate => candidate.Id == inspectionId);
        Assert.Equal(InspectionStatus.Completed, inspection.Status);
        Assert.NotNull(inspection.CompletedAtUtc);
    }

    [Fact]
    public async Task InspectionLifecycle_CompletedInspectionCannotBeModified()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = await CreateCompletedInspectionAsync(service, manager.Id, scenario.RepairOrder.Id, writeScope);
        var item = await writeScope.InspectionItems.FirstAsync(candidate => candidate.InspectionId == inspectionId);

        var updateResult = await service.UpdateInspectionItemsAsync(
            manager.Id,
            new UpdateInspectionItemsCommand
            {
                InspectionId = inspectionId,
                Items = [new InspectionItemUpdate
                {
                    InspectionItemId = item.Id,
                    Condition = InspectionCondition.Critical,
                }],
            });

        Assert.False(updateResult.Success);
        Assert.Equal(InspectionOperationFailureReason.InvalidLifecycleTransition, updateResult.FailureReason);
    }

    [Fact]
    public async Task InspectionLifecycle_CompletedInspectionCannotReturnToInProgress()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = await CreateCompletedInspectionAsync(service, manager.Id, scenario.RepairOrder.Id, writeScope);

        var startResult = await service.StartInspectionAsync(manager.Id, inspectionId);

        Assert.False(startResult.Success);
        Assert.Equal(InspectionOperationFailureReason.InvalidLifecycleTransition, startResult.FailureReason);
    }

    [Fact]
    public async Task InspectionLifecycle_Start_DoesNotStartRepairOrder()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = (await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id)).Value!;

        await service.StartInspectionAsync(manager.Id, inspectionId);

        var repairOrder = await writeScope.RepairOrders.SingleAsync(candidate => candidate.Id == scenario.RepairOrder.Id);
        Assert.Equal(RepairOrderStatus.Draft, repairOrder.Status);
    }

    [Fact]
    public async Task InspectionLifecycle_Complete_DoesNotCompleteRepairOrder()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix, RepairOrderStatus.InProgress);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        await CreateCompletedInspectionAsync(service, manager.Id, scenario.RepairOrder.Id, writeScope);

        var repairOrder = await writeScope.RepairOrders.SingleAsync(candidate => candidate.Id == scenario.RepairOrder.Id);
        Assert.Equal(RepairOrderStatus.InProgress, repairOrder.Status);
    }

    [Fact]
    public async Task InspectionItems_Update_DoesNotAcceptOtherInspectionItem()
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
        var inspectionAId = (await service.CreateInspectionAsync(manager.Id, scenarioA.RepairOrder.Id)).Value!;
        var inspectionBId = (await service.CreateInspectionAsync(manager.Id, scenarioB.RepairOrder.Id)).Value!;
        await service.StartInspectionAsync(manager.Id, inspectionAId);
        var foreignItem = await writeScope.InspectionItems.FirstAsync(candidate => candidate.InspectionId == inspectionBId);

        var result = await service.UpdateInspectionItemsAsync(
            manager.Id,
            new UpdateInspectionItemsCommand
            {
                InspectionId = inspectionAId,
                Items = [new InspectionItemUpdate
                {
                    InspectionItemId = foreignItem.Id,
                    Condition = InspectionCondition.Good,
                }],
            });

        Assert.False(result.Success);
        Assert.Equal(InspectionOperationFailureReason.ItemNotFound, result.FailureReason);
    }

    [Fact]
    public async Task InspectionItems_Update_DoesNotExposeOtherTenantItem()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);

        Guid foreignItemId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var managerB = await TestDataFactory.PersistUserAsync(scopeB, $"{suffix}-b");
            await TestDataFactory.PersistMembershipAsync(scopeB, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);
            var scenarioB = await CreateEligibleRepairOrderAsync(scopeB, organizationB.Id, suffix);
            var serviceB = CreateService(scopeB, new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow));
            var inspectionBId = (await serviceB.CreateInspectionAsync(managerB.Id, scenarioB.RepairOrder.Id)).Value!;
            foreignItemId = await scopeB.InspectionItems
                .Where(candidate => candidate.InspectionId == inspectionBId)
                .Select(candidate => candidate.Id)
                .FirstAsync();
        }

        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        var scenarioA = await CreateEligibleRepairOrderAsync(writeScopeA, organizationA.Id, suffix);
        var serviceA = CreateService(writeScopeA, new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        var inspectionAId = (await serviceA.CreateInspectionAsync(managerA.Id, scenarioA.RepairOrder.Id)).Value!;
        await serviceA.StartInspectionAsync(managerA.Id, inspectionAId);

        var result = await serviceA.UpdateInspectionItemsAsync(
            managerA.Id,
            new UpdateInspectionItemsCommand
            {
                InspectionId = inspectionAId,
                Items = [new InspectionItemUpdate
                {
                    InspectionItemId = foreignItemId,
                    Condition = InspectionCondition.Good,
                }],
            });

        Assert.False(result.Success);
        Assert.Equal(InspectionOperationFailureReason.ItemNotFound, result.FailureReason);
    }

    [Fact]
    public async Task InspectionItems_Update_CannotChangeTemplateIdentity()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = (await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id)).Value!;
        await service.StartInspectionAsync(manager.Id, inspectionId);
        var item = await writeScope.InspectionItems.FirstAsync(candidate => candidate.InspectionId == inspectionId);
        var originalName = item.Name;
        var originalSection = item.Section;
        var originalSortOrder = item.SortOrder;

        await service.UpdateInspectionItemsAsync(
            manager.Id,
            new UpdateInspectionItemsCommand
            {
                InspectionId = inspectionId,
                Items = [new InspectionItemUpdate
                {
                    InspectionItemId = item.Id,
                    Condition = InspectionCondition.Attention,
                    Notes = "Check soon",
                }],
            });

        var updated = await writeScope.InspectionItems.SingleAsync(candidate => candidate.Id == item.Id);
        Assert.Equal(originalName, updated.Name);
        Assert.Equal(originalSection, updated.Section);
        Assert.Equal(originalSortOrder, updated.SortOrder);
        Assert.Equal(InspectionCondition.Attention, updated.Condition);
    }

    [Fact]
    public async Task InspectionItems_Update_RejectsDuplicateSubmittedItemIds()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = (await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id)).Value!;
        await service.StartInspectionAsync(manager.Id, inspectionId);
        var item = await writeScope.InspectionItems.FirstAsync(candidate => candidate.InspectionId == inspectionId);

        var result = await service.UpdateInspectionItemsAsync(
            manager.Id,
            new UpdateInspectionItemsCommand
            {
                InspectionId = inspectionId,
                Items = [
                    new InspectionItemUpdate { InspectionItemId = item.Id, Condition = InspectionCondition.Good },
                    new InspectionItemUpdate { InspectionItemId = item.Id, Condition = InspectionCondition.Critical },
                ],
            });

        Assert.False(result.Success);
        Assert.Equal(InspectionOperationFailureReason.DuplicateItemSubmission, result.FailureReason);
    }

    [Fact]
    public async Task InspectionItems_Update_ValidConditionsPersist()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = (await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id)).Value!;
        await service.StartInspectionAsync(manager.Id, inspectionId);
        var item = await writeScope.InspectionItems.FirstAsync(candidate => candidate.InspectionId == inspectionId);

        await service.UpdateInspectionItemsAsync(
            manager.Id,
            new UpdateInspectionItemsCommand
            {
                InspectionId = inspectionId,
                Items = [new InspectionItemUpdate
                {
                    InspectionItemId = item.Id,
                    Condition = InspectionCondition.Critical,
                    Notes = "Needs service",
                }],
            });

        var updated = await writeScope.InspectionItems.SingleAsync(candidate => candidate.Id == item.Id);
        Assert.Equal(InspectionCondition.Critical, updated.Condition);
        Assert.Equal("Needs service", updated.Notes);
    }

    [Fact]
    public async Task InspectionItems_Update_UnsubmittedItemsRemainUnchanged()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = (await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id)).Value!;
        await service.StartInspectionAsync(manager.Id, inspectionId);
        var items = await writeScope.InspectionItems
            .Where(candidate => candidate.InspectionId == inspectionId)
            .OrderBy(candidate => candidate.SortOrder)
            .ToListAsync();

        await service.UpdateInspectionItemsAsync(
            manager.Id,
            new UpdateInspectionItemsCommand
            {
                InspectionId = inspectionId,
                Items = [new InspectionItemUpdate
                {
                    InspectionItemId = items[0].Id,
                    Condition = InspectionCondition.Good,
                }],
            });

        var untouched = await writeScope.InspectionItems.SingleAsync(candidate => candidate.Id == items[1].Id);
        Assert.Equal(InspectionCondition.NotChecked, untouched.Condition);
    }

    [Fact]
    public async Task InspectionExecution_AssignedTechnicianCanStartInspection()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);
        var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-tech");

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignedTechnicianScenarioAsync(writeScope, organization.Id, suffix, technicianUser.Id);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = (await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id)).Value!;

        var result = await service.StartInspectionAsync(technicianUser.Id, inspectionId);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task InspectionExecution_AssignedTechnicianCanUpdateItems()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);
        var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-tech");

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignedTechnicianScenarioAsync(writeScope, organization.Id, suffix, technicianUser.Id);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = (await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id)).Value!;
        await service.StartInspectionAsync(technicianUser.Id, inspectionId);
        var item = await writeScope.InspectionItems.FirstAsync(candidate => candidate.InspectionId == inspectionId);

        var result = await service.UpdateInspectionItemsAsync(
            technicianUser.Id,
            new UpdateInspectionItemsCommand
            {
                InspectionId = inspectionId,
                Items = [new InspectionItemUpdate
                {
                    InspectionItemId = item.Id,
                    Condition = InspectionCondition.Good,
                }],
            });

        Assert.True(result.Success);
    }

    [Fact]
    public async Task InspectionExecution_AssignedTechnicianCanCompleteInspection()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);
        var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-tech");

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignedTechnicianScenarioAsync(writeScope, organization.Id, suffix, technicianUser.Id);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = (await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id)).Value!;
        await service.StartInspectionAsync(technicianUser.Id, inspectionId);
        await MarkAllItemsInspectedAsync(service, technicianUser.Id, inspectionId, writeScope);

        var result = await service.CompleteInspectionAsync(technicianUser.Id, inspectionId);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task InspectionExecution_TechnicianCannotMutateAnotherTechniciansRepairOrderInspection()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);
        var technicianA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        var technicianB = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-b");

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenarioA = await CreateAssignedTechnicianScenarioAsync(writeScope, organization.Id, $"{suffix}-a", technicianA.Id);
        await CreateAssignedTechnicianScenarioAsync(writeScope, organization.Id, $"{suffix}-b", technicianB.Id);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = (await service.CreateInspectionAsync(manager.Id, scenarioA.RepairOrder.Id)).Value!;
        await service.StartInspectionAsync(manager.Id, inspectionId);

        var result = await service.UpdateInspectionItemsAsync(
            technicianB.Id,
            new UpdateInspectionItemsCommand
            {
                InspectionId = inspectionId,
                Items = [new InspectionItemUpdate
                {
                    InspectionItemId = (await writeScope.InspectionItems.FirstAsync(candidate => candidate.InspectionId == inspectionId)).Id,
                    Condition = InspectionCondition.Good,
                }],
            });

        Assert.False(result.Success);
        Assert.Equal(InspectionOperationFailureReason.Unauthorized, result.FailureReason);
    }

    [Fact]
    public async Task InspectionExecution_UnlinkedTechnicianCannotMutateInspection()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);
        var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-tech");

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var unlinkedTechnician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            organization.Id,
            scenario.Location.Id,
            $"{suffix}-unlinked");
        await TestDataFactory.PersistMembershipAsync(writeScope, organization.Id, technicianUser.Id, OrganizationMembershipRole.Technician);
        await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
            writeScope,
            organization.Id,
            scenario.RepairOrder.Id,
            unlinkedTechnician.StaffMember.Id,
            DefaultNow);

        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = (await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id)).Value!;
        await service.StartInspectionAsync(manager.Id, inspectionId);

        var result = await service.StartInspectionAsync(technicianUser.Id, inspectionId);

        Assert.False(result.Success);
        Assert.Equal(InspectionOperationFailureReason.TechnicianProfileNotLinked, result.FailureReason);
    }

    [Fact]
    public async Task InspectionExecution_InactiveStaffFailsClosed()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);
        var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-tech");

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignedTechnicianScenarioAsync(
            writeScope,
            organization.Id,
            suffix,
            technicianUser.Id,
            staffStatus: StaffStatus.Inactive);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = (await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id)).Value!;

        var result = await service.StartInspectionAsync(technicianUser.Id, inspectionId);

        Assert.False(result.Success);
        Assert.Equal(InspectionOperationFailureReason.StaffInactive, result.FailureReason);
    }

    [Fact]
    public async Task InspectionExecution_SuspendedMembershipFailsClosed()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);
        var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-tech");

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignedTechnicianScenarioAsync(
            writeScope,
            organization.Id,
            suffix,
            technicianUser.Id,
            membershipStatus: OrganizationMembershipStatus.Suspended);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = (await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id)).Value!;

        var result = await service.StartInspectionAsync(technicianUser.Id, inspectionId);

        Assert.False(result.Success);
        Assert.Equal(InspectionOperationFailureReason.MembershipInactive, result.FailureReason);
    }

    [Fact]
    public async Task InspectionExecution_RevokedMembershipFailsClosed()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);
        var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-tech");

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignedTechnicianScenarioAsync(
            writeScope,
            organization.Id,
            suffix,
            technicianUser.Id,
            membershipStatus: OrganizationMembershipStatus.Revoked);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = (await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id)).Value!;

        var result = await service.StartInspectionAsync(technicianUser.Id, inspectionId);

        Assert.False(result.Success);
        Assert.Equal(InspectionOperationFailureReason.MembershipInactive, result.FailureReason);
    }

    [Fact]
    public async Task InspectionExecution_OwnerOperationalTechnicianCanExecute()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var ownerTechnician = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner");

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignedTechnicianScenarioAsync(
            writeScope,
            organization.Id,
            suffix,
            ownerTechnician.Id,
            membershipRole: OrganizationMembershipRole.Owner);
        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = (await service.CreateInspectionAsync(ownerTechnician.Id, scenario.RepairOrder.Id)).Value!;

        var startResult = await service.StartInspectionAsync(ownerTechnician.Id, inspectionId);
        Assert.True(startResult.Success);
    }

    [Fact]
    public async Task InspectionExecution_StaffPositionWithoutMembershipIsDenied()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);
        var userWithoutMembership = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-no-membership");

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var technician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            organization.Id,
            scenario.Location.Id,
            suffix);
        await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
            writeScope,
            organization.Id,
            scenario.RepairOrder.Id,
            technician.StaffMember.Id,
            DefaultNow);

        var service = CreateService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
        var inspectionId = (await service.CreateInspectionAsync(manager.Id, scenario.RepairOrder.Id)).Value!;

        var result = await service.StartInspectionAsync(userWithoutMembership.Id, inspectionId);

        Assert.False(result.Success);
        Assert.Equal(InspectionOperationFailureReason.MembershipInactive, result.FailureReason);
    }

    private static IInspectionManagementService CreateService(
        AppDbContext context,
        IOrganizationContext organizationContext,
        TimeProvider timeProvider) =>
        new InspectionManagementService(context, organizationContext, timeProvider);

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

    private static async Task<AssignedTechnicianScenario> CreateAssignedTechnicianScenarioAsync(
        AppDbContext context,
        Guid organizationId,
        string suffix,
        Guid technicianUserId,
        OrganizationMembershipStatus membershipStatus = OrganizationMembershipStatus.Active,
        OrganizationMembershipRole membershipRole = OrganizationMembershipRole.Technician,
        StaffStatus staffStatus = StaffStatus.Active,
        bool createMembership = true)
    {
        var scenario = await CreateEligibleRepairOrderAsync(context, organizationId, suffix);

        if (createMembership)
        {
            await TestDataFactory.PersistMembershipAsync(
                context,
                organizationId,
                technicianUserId,
                membershipRole,
                membershipStatus);
        }

        var technician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            context,
            organizationId,
            scenario.Location.Id,
            $"{suffix}-linked",
            technicianUserId,
            staffStatus);

        await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
            context,
            organizationId,
            scenario.RepairOrder.Id,
            technician.StaffMember.Id,
            DefaultNow);

        return new AssignedTechnicianScenario(scenario.Location, scenario.RepairOrder, technician);
    }

    private static async Task<Guid> CreateCompletedInspectionAsync(
        IInspectionManagementService service,
        Guid actorUserId,
        Guid repairOrderId,
        AppDbContext context)
    {
        var inspectionId = (await service.CreateInspectionAsync(actorUserId, repairOrderId)).Value!;
        await service.StartInspectionAsync(actorUserId, inspectionId);
        await MarkAllItemsInspectedAsync(service, actorUserId, inspectionId, context);
        await service.CompleteInspectionAsync(actorUserId, inspectionId);
        return inspectionId;
    }

    private static async Task MarkAllItemsInspectedAsync(
        IInspectionManagementService service,
        Guid actorUserId,
        Guid inspectionId,
        AppDbContext context)
    {
        var items = await context.InspectionItems
            .Where(candidate => candidate.InspectionId == inspectionId)
            .ToListAsync();

        await service.UpdateInspectionItemsAsync(
            actorUserId,
            new UpdateInspectionItemsCommand
            {
                InspectionId = inspectionId,
                Items = items
                    .Select(item => new InspectionItemUpdate
                    {
                        InspectionItemId = item.Id,
                        Condition = InspectionCondition.Good,
                    })
                    .ToList(),
            });
    }

    private sealed record EligibleRepairOrderScenario(
        Domain.Organizations.WorkshopLocation Location,
        RepairOrder RepairOrder);

    private sealed record AssignedTechnicianScenario(
        Domain.Organizations.WorkshopLocation Location,
        RepairOrder RepairOrder,
        (StaffMember StaffMember, StaffLocationAssignment LocationAssignment) Technician);
}

[Collection(PostgreSqlCollection.Name)]
public sealed class InspectionManagerAuthorizationTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task InspectionManager_AllowsOwner()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Owner, shouldSucceed: true);
    }

    [Fact]
    public async Task InspectionManager_AllowsAdministrator()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Administrator, shouldSucceed: true);
    }

    [Fact]
    public async Task InspectionManager_AllowsServiceAdvisor()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.ServiceAdvisor, shouldSucceed: true);
    }

    [Fact]
    public async Task InspectionManager_RejectsTechnician()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Technician, shouldSucceed: false);
    }

    [Fact]
    public async Task InspectionManager_RejectsViewer()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Viewer, shouldSucceed: false);
    }

    [Fact]
    public async Task InspectionManager_ReflectsDatabaseRoleChange()
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
        var handler = new InspectionManagerAuthorizationHandler(writeScope, organizationContext);

        var initialContext = new AuthorizationHandlerContext(
            [new InspectionManagerRequirement()],
            CreatePrincipal(advisor.Id),
            resource: null);
        await handler.HandleAsync(initialContext);
        Assert.True(initialContext.HasSucceeded);

        var advisorMembership = await writeScope.OrganizationMemberships
            .SingleAsync(membership => membership.UserId == advisor.Id);
        advisorMembership.ChangeRole(OrganizationMembershipRole.Technician);
        await writeScope.SaveChangesAsync();

        var afterContext = new AuthorizationHandlerContext(
            [new InspectionManagerRequirement()],
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
        var handler = new InspectionManagerAuthorizationHandler(writeScope, organizationContext);

        var context = new AuthorizationHandlerContext(
            [new InspectionManagerRequirement()],
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
