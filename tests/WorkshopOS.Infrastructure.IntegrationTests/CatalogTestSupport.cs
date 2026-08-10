using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Catalog;
using WorkshopOS.Application.Estimates;
using WorkshopOS.Application.Inventory;
using WorkshopOS.Domain.Catalog;
using WorkshopOS.Domain.Inventory;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Catalog;
using WorkshopOS.Infrastructure.Estimates;
using WorkshopOS.Infrastructure.Inventory;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

internal static class CatalogTestSupport
{
    public static readonly DateTimeOffset DefaultNow = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    public static IServiceCatalogService CreateServiceCatalogService(
        AppDbContext context,
        IOrganizationContext organizationContext) =>
        new ServiceCatalogService(context, organizationContext);

    public static IPartCatalogService CreatePartCatalogService(
        AppDbContext context,
        IOrganizationContext organizationContext) =>
        new PartCatalogService(context, organizationContext);

    public static IInventoryManagementService CreateInventoryService(
        AppDbContext context,
        IOrganizationContext organizationContext,
        TimeProvider timeProvider) =>
        new InventoryManagementService(context, organizationContext, timeProvider);

    public static IEstimateManagementService CreateEstimateService(
        AppDbContext context,
        IOrganizationContext organizationContext,
        TimeProvider timeProvider) =>
        new EstimateManagementService(
            context,
            organizationContext,
            new EstimateNumberGenerator(),
            timeProvider);

    public static async Task<CatalogScenario> CreateCatalogScenarioAsync(
        DatabaseTransactionScope scope,
        string suffix,
        OrganizationMembershipRole role = OrganizationMembershipRole.Owner)
    {
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, role);
        return new CatalogScenario(organization.Id, manager.Id);
    }

    public static async Task<CatalogLocationScenario> CreateCatalogLocationScenarioAsync(
        AppDbContext context,
        Guid organizationId,
        string suffix)
    {
        var location = await TestDataFactory.PersistWorkshopLocationAsync(context, organizationId, suffix);
        return new CatalogLocationScenario(location);
    }

    public static async Task<Guid> CreateServiceAsync(
        IServiceCatalogService service,
        Guid actorUserId,
        string suffix,
        string? code = null,
        decimal price = 100m,
        string name = "Oil Change")
    {
        var result = await service.CreateServiceAsync(
            actorUserId,
            new CreateServiceCatalogItemCommand
            {
                Code = code ?? $"SVC-{suffix}",
                Name = name,
                Description = $"Service {suffix}",
                DefaultUnitPrice = price,
            });

        Assert.True(result.Success);
        return result.Value!;
    }

    public static async Task<Guid> CreatePartAsync(
        IPartCatalogService service,
        Guid actorUserId,
        string suffix,
        string? sku = null,
        decimal price = 25m,
        string name = "Oil Filter")
    {
        var result = await service.CreatePartAsync(
            actorUserId,
            new CreatePartCatalogItemCommand
            {
                Sku = sku ?? $"PART-{suffix}",
                Name = name,
                Description = $"Part {suffix}",
                DefaultUnitPrice = price,
            });

        Assert.True(result.Success);
        return result.Value!;
    }

    public static async Task<InventoryAdjustmentResult> AdjustInventoryAsync(
        IInventoryManagementService service,
        Guid actorUserId,
        Guid partCatalogItemId,
        Guid workshopLocationId,
        PartInventoryMovementType movementType,
        decimal quantity,
        string? reason = null)
    {
        var result = await service.AdjustInventoryAsync(
            actorUserId,
            new AdjustInventoryCommand
            {
                PartCatalogItemId = partCatalogItemId,
                WorkshopLocationId = workshopLocationId,
                MovementType = movementType,
                Quantity = quantity,
                Reason = reason,
            });

        return result;
    }

    public static async Task<decimal> GetTotalInventoryQuantityAsync(AppDbContext context) =>
        await context.PartInventoryBalances.SumAsync(balance => balance.QuantityOnHand);

    public static async Task<Guid> CreateDraftEstimateAsync(
        IEstimateManagementService estimateService,
        AppDbContext context,
        Guid organizationId,
        Guid actorUserId,
        string suffix)
    {
        var location = await TestDataFactory.PersistWorkshopLocationAsync(context, organizationId, $"{suffix}-estimate");
        var customer = await TestDataFactory.PersistCustomerAsync(context, organizationId, $"{suffix}-estimate");
        var vehicle = await TestDataFactory.PersistVehicleAsync(context, organizationId, $"{suffix}-estimate", customer.Id);
        var repairOrder = await TestDataFactory.PersistRepairOrderAsync(
            context,
            organizationId,
            location.Id,
            customer.Id,
            vehicle.Id,
            $"{suffix}-estimate",
            DefaultNow);

        var result = await estimateService.CreateEstimateAsync(actorUserId, repairOrder.Id);
        Assert.True(result.Success);
        return result.Value!;
    }

    public static async Task<WorkshopLocation> PersistInactiveLocationAsync(
        AppDbContext context,
        Guid organizationId,
        string suffix)
    {
        var location = new WorkshopLocation(organizationId, $"Inactive {suffix}", suffix, isActive: false);
        context.WorkshopLocations.Add(location);
        await context.SaveChangesAsync();
        return location;
    }

    public sealed record CatalogScenario(Guid OrganizationId, Guid ManagerId);

    public sealed record CatalogLocationScenario(WorkshopLocation Location);
}
