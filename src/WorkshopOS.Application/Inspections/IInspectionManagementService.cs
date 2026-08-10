using WorkshopOS.Domain.Inspections;

namespace WorkshopOS.Application.Inspections;

public interface IInspectionManagementService
{
    Task<InspectionListResult> ListInspectionsAsync(
        InspectionListQuery query,
        CancellationToken cancellationToken = default);

    Task<InspectionDetails?> GetInspectionDetailsAsync(
        Guid inspectionId,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<RepairOrderInspectionSummary?> GetRepairOrderInspectionSummaryAsync(
        Guid repairOrderId,
        CancellationToken cancellationToken = default);

    Task<InspectionOperationResult<Guid>> CreateInspectionAsync(
        Guid actorUserId,
        Guid repairOrderId,
        CancellationToken cancellationToken = default);

    Task<InspectionOperationResult> StartInspectionAsync(
        Guid actorUserId,
        Guid inspectionId,
        CancellationToken cancellationToken = default);

    Task<InspectionOperationResult> UpdateInspectionItemsAsync(
        Guid actorUserId,
        UpdateInspectionItemsCommand command,
        CancellationToken cancellationToken = default);

    Task<InspectionOperationResult> CompleteInspectionAsync(
        Guid actorUserId,
        Guid inspectionId,
        CancellationToken cancellationToken = default);
}

public sealed record InspectionListQuery
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    public string? Search { get; init; }

    public InspectionStatus? Status { get; init; }

    public Guid? RepairOrderId { get; init; }

    public Guid? WorkshopLocationId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;
}

public sealed class InspectionListResult
{
    public required IReadOnlyList<InspectionListItem> Items { get; init; }

    public required int TotalCount { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }
}

public sealed class InspectionListItem
{
    public required Guid InspectionId { get; init; }

    public required Guid RepairOrderId { get; init; }

    public required string RepairOrderNumber { get; init; }

    public required InspectionStatus Status { get; init; }

    public required string CustomerDisplayName { get; init; }

    public required string VehicleSummary { get; init; }

    public required string WorkshopLocationName { get; init; }

    public required int TotalItems { get; init; }

    public required int InspectedItems { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }
}

public sealed class InspectionDetails
{
    public required Guid InspectionId { get; init; }

    public required Guid RepairOrderId { get; init; }

    public required string RepairOrderNumber { get; init; }

    public required InspectionStatus Status { get; init; }

    public required string CustomerDisplayName { get; init; }

    public required string VehicleSummary { get; init; }

    public required string WorkshopLocationName { get; init; }

    public required IReadOnlyList<InspectionSectionDetails> Sections { get; init; }

    public required InspectionResultSummary Summary { get; init; }

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    public bool CanStart { get; init; }

    public bool CanUpdateItems { get; init; }

    public bool CanComplete { get; init; }

    public bool CanUploadMedia { get; init; }

    public bool CanRemoveMedia { get; init; }

    public int ActivePhotoCount { get; init; }
}

public sealed class InspectionSectionDetails
{
    public required string Section { get; init; }

    public required IReadOnlyList<InspectionItemDetails> Items { get; init; }
}

public sealed class InspectionItemDetails
{
    public required Guid InspectionItemId { get; init; }

    public required string Name { get; init; }

    public required InspectionCondition Condition { get; init; }

    public string? Notes { get; init; }

    public required int SortOrder { get; init; }

    public required IReadOnlyList<InspectionMediaItem> Media { get; init; }
}

public sealed class InspectionMediaItem
{
    public required Guid MediaId { get; init; }

    public required Guid InspectionItemId { get; init; }

    public required string ContentType { get; init; }

    public required long LengthBytes { get; init; }

    public string? Caption { get; init; }

    public required DateTimeOffset UploadedAtUtc { get; init; }
}

public sealed class InspectionResultSummary
{
    public required int Total { get; init; }

    public required int Inspected { get; init; }

    public required int Good { get; init; }

    public required int Attention { get; init; }

    public required int Critical { get; init; }

    public required int Monitor { get; init; }
}

public sealed class RepairOrderInspectionSummary
{
    public required int InspectionCount { get; init; }

    public Guid? LatestInspectionId { get; init; }

    public InspectionStatus? LatestInspectionStatus { get; init; }

    public DateTimeOffset? LatestInspectionCreatedAtUtc { get; init; }

    public DateTimeOffset? LatestInspectionCompletedAtUtc { get; init; }
}

public sealed record UpdateInspectionItemsCommand
{
    public required Guid InspectionId { get; init; }

    public required IReadOnlyList<InspectionItemUpdate> Items { get; init; }
}

public sealed record InspectionItemUpdate
{
    public required Guid InspectionItemId { get; init; }

    public required InspectionCondition Condition { get; init; }

    public string? Notes { get; init; }
}

public sealed class InspectionOperationResult
{
    public bool Success { get; init; }

    public InspectionOperationFailureReason? FailureReason { get; init; }

    public static InspectionOperationResult Succeeded() => new() { Success = true };

    public static InspectionOperationResult Failed(InspectionOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public sealed class InspectionOperationResult<T>
{
    public bool Success { get; init; }

    public T? Value { get; init; }

    public InspectionOperationFailureReason? FailureReason { get; init; }

    public static InspectionOperationResult<T> Succeeded(T value) =>
        new() { Success = true, Value = value };

    public static InspectionOperationResult<T> Failed(InspectionOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public enum InspectionOperationFailureReason
{
    OrganizationUnresolved = 1,
    InspectionNotFound = 2,
    RepairOrderNotFound = 3,
    Unauthorized = 4,
    InvalidInput = 5,
    RepairOrderNotEligible = 6,
    InvalidLifecycleTransition = 7,
    ItemsNotFullyInspected = 8,
    EmptyInspection = 9,
    ConcurrencyConflict = 10,
    MembershipInactive = 11,
    TechnicianProfileNotLinked = 12,
    StaffInactive = 13,
    ItemNotFound = 14,
    DuplicateItemSubmission = 15,
}
