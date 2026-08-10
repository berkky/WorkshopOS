using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Inspections;

namespace WorkshopOS.Application.Estimates;

public interface IEstimateManagementService
{
    Task<EstimateListResult> ListEstimatesAsync(
        EstimateListQuery query,
        CancellationToken cancellationToken = default);

    Task<EstimateDetails?> GetEstimateDetailsAsync(
        Guid estimateId,
        CancellationToken cancellationToken = default);

    Task<RepairOrderEstimateSummary?> GetRepairOrderEstimateSummaryAsync(
        Guid repairOrderId,
        CancellationToken cancellationToken = default);

    Task<EstimateOperationResult<Guid>> CreateEstimateAsync(
        Guid actorUserId,
        Guid repairOrderId,
        CancellationToken cancellationToken = default);

    Task<EstimateOperationResult<Guid>> AddEstimateItemAsync(
        Guid actorUserId,
        AddEstimateItemCommand command,
        CancellationToken cancellationToken = default);

    Task<EstimateOperationResult> UpdateEstimateItemAsync(
        Guid actorUserId,
        UpdateEstimateItemCommand command,
        CancellationToken cancellationToken = default);

    Task<EstimateOperationResult> RemoveEstimateItemAsync(
        Guid actorUserId,
        Guid estimateId,
        Guid estimateItemId,
        CancellationToken cancellationToken = default);

    Task<EstimateOperationResult> UpdateEstimateCustomerMessageAsync(
        Guid actorUserId,
        Guid estimateId,
        string? customerMessage,
        CancellationToken cancellationToken = default);

    Task<EstimateOperationResult> PresentForApprovalAsync(
        Guid actorUserId,
        Guid estimateId,
        CancellationToken cancellationToken = default);

    Task<EstimateOperationResult> RecordCustomerApprovalAsync(
        Guid actorUserId,
        Guid estimateId,
        CancellationToken cancellationToken = default);

    Task<EstimateOperationResult> RecordCustomerDeclineAsync(
        Guid actorUserId,
        Guid estimateId,
        CancellationToken cancellationToken = default);

    Task<EstimateOperationResult<Guid>> AddServiceCatalogItemAsync(
        Guid actorUserId,
        AddServiceCatalogItemToEstimateCommand command,
        CancellationToken cancellationToken = default);

    Task<EstimateOperationResult<Guid>> AddPartCatalogItemAsync(
        Guid actorUserId,
        AddPartCatalogItemToEstimateCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record EstimateListQuery
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    public string? Search { get; init; }

    public EstimateStatus? Status { get; init; }

    public Guid? RepairOrderId { get; init; }

    public Guid? WorkshopLocationId { get; init; }

    public DateTimeOffset? CreatedFromUtc { get; init; }

    public DateTimeOffset? CreatedToUtc { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;
}

public sealed class EstimateListResult
{
    public required IReadOnlyList<EstimateListItem> Items { get; init; }

    public required int TotalCount { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }
}

public sealed class EstimateListItem
{
    public required Guid EstimateId { get; init; }

    public required string Number { get; init; }

    public required EstimateStatus Status { get; init; }

    public required string CurrencyCode { get; init; }

    public required decimal Total { get; init; }

    public required Guid RepairOrderId { get; init; }

    public required string RepairOrderNumber { get; init; }

    public required string CustomerDisplayName { get; init; }

    public required string VehicleSummary { get; init; }

    public required string WorkshopLocationName { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? SentAtUtc { get; init; }
}

public sealed class EstimateDetails
{
    public required Guid EstimateId { get; init; }

    public required string Number { get; init; }

    public required EstimateStatus Status { get; init; }

    public required Guid RepairOrderId { get; init; }

    public required string RepairOrderNumber { get; init; }

    public required string CustomerDisplayName { get; init; }

    public required string VehicleSummary { get; init; }

    public required string WorkshopLocationName { get; init; }

    public required Guid WorkshopLocationId { get; init; }

    public required string CurrencyCode { get; init; }

    public required decimal Total { get; init; }

    public string? CustomerMessage { get; init; }

    public required IReadOnlyList<EstimateItemDetails> Items { get; init; }

    public required IReadOnlyList<InspectionFindingContext> InspectionFindings { get; init; }

    public Guid? LatestInspectionId { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    public DateTimeOffset? SentAtUtc { get; init; }

    public DateTimeOffset? ApprovedAtUtc { get; init; }

    public DateTimeOffset? DeclinedAtUtc { get; init; }

    public bool CanEditItems { get; init; }

    public bool CanPresent { get; init; }

    public bool CanRecordApproval { get; init; }

    public bool CanRecordDecline { get; init; }
}

public sealed class EstimateItemDetails
{
    public required Guid EstimateItemId { get; init; }

    public required EstimateItemType Type { get; init; }

    public required string Description { get; init; }

    public required decimal Quantity { get; init; }

    public required decimal UnitPrice { get; init; }

    public required decimal LineTotal { get; init; }

    public required int SortOrder { get; init; }
}

public sealed class InspectionFindingContext
{
    public required string Section { get; init; }

    public required string Name { get; init; }

    public required InspectionCondition Condition { get; init; }

    public string? Notes { get; init; }
}

public sealed class RepairOrderEstimateSummary
{
    public required int EstimateCount { get; init; }

    public Guid? LatestEstimateId { get; init; }

    public EstimateStatus? LatestEstimateStatus { get; init; }

    public decimal? LatestEstimateTotal { get; init; }

    public string? LatestEstimateCurrencyCode { get; init; }

    public DateTimeOffset? LatestEstimateCreatedAtUtc { get; init; }
}

public sealed record AddEstimateItemCommand
{
    public required Guid EstimateId { get; init; }

    public EstimateItemType Type { get; init; } = EstimateItemType.Service;

    public required string Description { get; init; }

    public required decimal Quantity { get; init; }

    public required decimal UnitPrice { get; init; }
}

public sealed record UpdateEstimateItemCommand
{
    public required Guid EstimateId { get; init; }

    public required Guid EstimateItemId { get; init; }

    public EstimateItemType Type { get; init; } = EstimateItemType.Service;

    public required string Description { get; init; }

    public required decimal Quantity { get; init; }

    public required decimal UnitPrice { get; init; }
}

public sealed record AddServiceCatalogItemToEstimateCommand
{
    public required Guid EstimateId { get; init; }

    public required Guid ServiceCatalogItemId { get; init; }

    public required decimal Quantity { get; init; }
}

public sealed record AddPartCatalogItemToEstimateCommand
{
    public required Guid EstimateId { get; init; }

    public required Guid PartCatalogItemId { get; init; }

    public required decimal Quantity { get; init; }
}

public sealed class EstimateOperationResult
{
    public bool Success { get; init; }

    public EstimateOperationFailureReason? FailureReason { get; init; }

    public static EstimateOperationResult Succeeded() => new() { Success = true };

    public static EstimateOperationResult Failed(EstimateOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public sealed class EstimateOperationResult<T>
{
    public bool Success { get; init; }

    public T? Value { get; init; }

    public EstimateOperationFailureReason? FailureReason { get; init; }

    public static EstimateOperationResult<T> Succeeded(T value) =>
        new() { Success = true, Value = value };

    public static EstimateOperationResult<T> Failed(EstimateOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public enum EstimateOperationFailureReason
{
    OrganizationUnresolved = 1,
    EstimateNotFound = 2,
    RepairOrderNotFound = 3,
    Unauthorized = 4,
    InvalidInput = 5,
    RepairOrderNotEligible = 6,
    InvalidLifecycleTransition = 7,
    EmptyEstimate = 8,
    ConcurrencyConflict = 9,
    MembershipInactive = 10,
    ItemNotFound = 11,
    ItemLimitExceeded = 12,
    ItemBelongsToAnotherEstimate = 13,
    CatalogItemNotFound = 14,
    CatalogItemInactive = 15,
    CurrencyMismatch = 16,
}
