using WorkshopOS.Domain.RepairOrders;

namespace WorkshopOS.Application.RepairOrders;

public interface IRepairOrderManagementService
{
    Task<RepairOrderListResult> ListRepairOrdersAsync(
        RepairOrderListQuery query,
        CancellationToken cancellationToken = default);

    Task<RepairOrderDetails?> GetRepairOrderDetailsAsync(
        Guid repairOrderId,
        CancellationToken cancellationToken = default);

    Task<RepairOrderOperationResult<Guid>> CreateRepairOrderAsync(
        CreateRepairOrderCommand command,
        CancellationToken cancellationToken = default);

    Task<RepairOrderOperationResult<Guid>> CreateRepairOrderFromAppointmentAsync(
        CreateRepairOrderFromAppointmentCommand command,
        CancellationToken cancellationToken = default);

    Task<RepairOrderOperationResult> UpdateRepairOrderIntakeAsync(
        UpdateRepairOrderIntakeCommand command,
        CancellationToken cancellationToken = default);

    Task<RepairOrderOperationResult> StartRepairOrderAsync(
        Guid repairOrderId,
        CancellationToken cancellationToken = default);

    Task<RepairOrderOperationResult> CompleteRepairOrderAsync(
        Guid repairOrderId,
        CancellationToken cancellationToken = default);

    Task<RepairOrderOperationResult> CancelRepairOrderAsync(
        Guid repairOrderId,
        CancellationToken cancellationToken = default);
}

public sealed record RepairOrderListQuery
{
    public const int DefaultPageSize = RepairOrderInputValidator.DefaultPageSize;

    public const int MaxPageSize = RepairOrderInputValidator.MaxPageSize;

    public string? Search { get; init; }

    public RepairOrderStatus? Status { get; init; }

    public Guid? WorkshopLocationId { get; init; }

    public Guid? CustomerId { get; init; }

    public Guid? VehicleId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;
}

public sealed record CreateRepairOrderCommand
{
    public required Guid WorkshopLocationId { get; init; }

    public required Guid CustomerId { get; init; }

    public required Guid VehicleId { get; init; }

    public string? CustomerConcern { get; init; }

    public string? InternalNotes { get; init; }

    public int? Odometer { get; init; }
}

public sealed record CreateRepairOrderFromAppointmentCommand
{
    public required Guid AppointmentId { get; init; }

    public string? CustomerConcern { get; init; }

    public string? InternalNotes { get; init; }

    public int? Odometer { get; init; }
}

public sealed record UpdateRepairOrderIntakeCommand
{
    public required Guid RepairOrderId { get; init; }

    public string? CustomerConcern { get; init; }

    public string? InternalNotes { get; init; }

    public int? Odometer { get; init; }
}

public sealed class RepairOrderListResult
{
    public required IReadOnlyList<RepairOrderListItem> Items { get; init; }

    public required int TotalCount { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }
}

public sealed class RepairOrderListItem
{
    public required Guid RepairOrderId { get; init; }

    public required string Number { get; init; }

    public required RepairOrderStatus Status { get; init; }

    public required string CustomerDisplayName { get; init; }

    public required string VehicleSummary { get; init; }

    public required string WorkshopLocationName { get; init; }

    public required DateTimeOffset OpenedAtUtc { get; init; }
}

public sealed class RepairOrderDetails
{
    public required Guid RepairOrderId { get; init; }

    public required string Number { get; init; }

    public required RepairOrderStatus Status { get; init; }

    public required Guid WorkshopLocationId { get; init; }

    public required string WorkshopLocationName { get; init; }

    public Guid? AppointmentId { get; init; }

    public required Guid CustomerId { get; init; }

    public required string CustomerDisplayName { get; init; }

    public required Guid VehicleId { get; init; }

    public required string VehicleSummary { get; init; }

    public string? CustomerConcern { get; init; }

    public string? InternalNotes { get; init; }

    public int? Odometer { get; init; }

    public required DateTimeOffset OpenedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed class RepairOrderOperationResult
{
    public bool Success { get; init; }

    public RepairOrderOperationFailureReason? FailureReason { get; init; }

    public static RepairOrderOperationResult Succeeded() => new() { Success = true };

    public static RepairOrderOperationResult Failed(RepairOrderOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public sealed class RepairOrderOperationResult<T>
{
    public bool Success { get; init; }

    public T? Value { get; init; }

    public RepairOrderOperationFailureReason? FailureReason { get; init; }

    public static RepairOrderOperationResult<T> Succeeded(T value) =>
        new() { Success = true, Value = value };

    public static RepairOrderOperationResult<T> Failed(RepairOrderOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public enum RepairOrderOperationFailureReason
{
    OrganizationUnresolved = 1,
    RepairOrderNotFound = 2,
    InvalidInput = 3,
    WorkshopLocationNotFound = 4,
    CustomerNotFound = 5,
    VehicleNotFound = 6,
    AppointmentNotFound = 7,
    CustomerVehicleMismatch = 8,
    AppointmentMismatch = 9,
    DuplicateAppointmentRepairOrder = 10,
    ConcurrencyConflict = 11,
    InvalidLifecycleTransition = 12,
}
