using WorkshopOS.Domain.Inventory;

namespace WorkshopOS.Application.Inventory;

public interface IInventoryManagementService
{
    Task<InventoryListResult> ListInventoryAsync(
        InventoryListQuery query,
        CancellationToken cancellationToken = default);

    Task<InventoryMovementHistoryResult> GetMovementHistoryAsync(
        InventoryMovementHistoryQuery query,
        CancellationToken cancellationToken = default);

    Task<decimal?> GetQuantityOnHandAsync(
        Guid partCatalogItemId,
        Guid workshopLocationId,
        CancellationToken cancellationToken = default);

    Task<InventoryAdjustmentResult> AdjustInventoryAsync(
        Guid actorUserId,
        AdjustInventoryCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record InventoryListQuery
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    public Guid? WorkshopLocationId { get; init; }

    public Guid? PartCatalogItemId { get; init; }

    public bool? LowOrZeroStockOnly { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;
}

public sealed class InventoryListResult
{
    public required IReadOnlyList<InventoryBalanceItem> Items { get; init; }

    public required int TotalCount { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }
}

public sealed class InventoryBalanceItem
{
    public required Guid PartCatalogItemId { get; init; }

    public required string PartSku { get; init; }

    public required string PartName { get; init; }

    public required bool PartIsActive { get; init; }

    public required Guid WorkshopLocationId { get; init; }

    public required string WorkshopLocationName { get; init; }

    public required decimal QuantityOnHand { get; init; }
}

public sealed record InventoryMovementHistoryQuery
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    public required Guid PartCatalogItemId { get; init; }

    public required Guid WorkshopLocationId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;
}

public sealed class InventoryMovementHistoryResult
{
    public required IReadOnlyList<InventoryMovementItem> Items { get; init; }

    public required int TotalCount { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }
}

public sealed class InventoryMovementItem
{
    public required Guid MovementId { get; init; }

    public required PartInventoryMovementType MovementType { get; init; }

    public required decimal QuantityDelta { get; init; }

    public required decimal BalanceAfter { get; init; }

    public string? Reason { get; init; }

    public required Guid RecordedByUserId { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }
}

public sealed record AdjustInventoryCommand
{
    public required Guid PartCatalogItemId { get; init; }

    public required Guid WorkshopLocationId { get; init; }

    public required PartInventoryMovementType MovementType { get; init; }

    public required decimal Quantity { get; init; }

    public string? Reason { get; init; }
}

public sealed class InventoryAdjustmentResult
{
    public bool Success { get; init; }

    public Guid? MovementId { get; init; }

    public decimal? BalanceAfter { get; init; }

    public InventoryAdjustmentFailureReason? FailureReason { get; init; }

    public static InventoryAdjustmentResult Succeeded(Guid movementId, decimal balanceAfter) =>
        new() { Success = true, MovementId = movementId, BalanceAfter = balanceAfter };

    public static InventoryAdjustmentResult Failed(InventoryAdjustmentFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public enum InventoryAdjustmentFailureReason
{
    OrganizationUnresolved = 1,
    Unauthorized = 2,
    InvalidInput = 3,
    PartNotFound = 4,
    LocationNotFound = 5,
    PartInactive = 6,
    LocationInactive = 7,
    InsufficientStock = 8,
    ConcurrencyConflict = 9,
}
