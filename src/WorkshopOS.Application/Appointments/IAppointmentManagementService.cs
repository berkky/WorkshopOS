using WorkshopOS.Domain.Appointments;

namespace WorkshopOS.Application.Appointments;

public interface IAppointmentManagementService
{
    Task<AppointmentListResult> ListAppointmentsAsync(
        AppointmentListQuery query,
        CancellationToken cancellationToken = default);

    Task<AppointmentCalendarResult> GetCalendarAppointmentsAsync(
        AppointmentCalendarQuery query,
        CancellationToken cancellationToken = default);

    Task<AppointmentDetails?> GetAppointmentDetailsAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    Task<AppointmentOperationResult<Guid>> CreateAppointmentAsync(
        CreateAppointmentCommand command,
        CancellationToken cancellationToken = default);

    Task<AppointmentOperationResult> UpdateAppointmentAsync(
        UpdateAppointmentCommand command,
        CancellationToken cancellationToken = default);

    Task<AppointmentOperationResult> RescheduleAppointmentAsync(
        RescheduleAppointmentCommand command,
        CancellationToken cancellationToken = default);

    Task<AppointmentOperationResult> CancelAppointmentAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    Task<string?> ResolveSchedulingTimeZoneAsync(
        Guid workshopLocationId,
        CancellationToken cancellationToken = default);
}

public sealed record AppointmentListQuery
{
    public const int DefaultPageSize = AppointmentInputValidator.DefaultPageSize;

    public const int MaxPageSize = AppointmentInputValidator.MaxPageSize;

    public Guid? WorkshopLocationId { get; init; }

    public Guid? CustomerId { get; init; }

    public Guid? VehicleId { get; init; }

    public AppointmentStatus? Status { get; init; }

    public DateTimeOffset? FromUtc { get; init; }

    public DateTimeOffset? ToUtc { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;
}

public sealed record AppointmentCalendarQuery
{
    public required DateOnly WeekStartLocal { get; init; }

    public Guid? WorkshopLocationId { get; init; }

    public AppointmentStatus? Status { get; init; }
}

public sealed record CreateAppointmentCommand
{
    public required Guid WorkshopLocationId { get; init; }

    public required Guid CustomerId { get; init; }

    public required Guid VehicleId { get; init; }

    public required DateTimeOffset ScheduledStartUtc { get; init; }

    public required DateTimeOffset ScheduledEndUtc { get; init; }

    public string? CustomerConcern { get; init; }

    public string? InternalNotes { get; init; }
}

public sealed record UpdateAppointmentCommand
{
    public required Guid AppointmentId { get; init; }

    public required Guid WorkshopLocationId { get; init; }

    public required Guid CustomerId { get; init; }

    public required Guid VehicleId { get; init; }

    public string? CustomerConcern { get; init; }

    public string? InternalNotes { get; init; }
}

public sealed record RescheduleAppointmentCommand
{
    public required Guid AppointmentId { get; init; }

    public required Guid WorkshopLocationId { get; init; }

    public required DateTimeOffset ScheduledStartUtc { get; init; }

    public required DateTimeOffset ScheduledEndUtc { get; init; }
}

public sealed class AppointmentListResult
{
    public required IReadOnlyList<AppointmentListItem> Items { get; init; }

    public required int TotalCount { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }
}

public sealed class AppointmentListItem
{
    public required Guid AppointmentId { get; init; }

    public required Guid WorkshopLocationId { get; init; }

    public required DateTimeOffset ScheduledStartUtc { get; init; }

    public required DateTimeOffset ScheduledEndUtc { get; init; }

    public required AppointmentStatus Status { get; init; }

    public required string WorkshopLocationName { get; init; }

    public required string CustomerDisplayName { get; init; }

    public required string VehicleSummary { get; init; }
}

public sealed class AppointmentCalendarResult
{
    public required DateOnly WeekStartLocal { get; init; }

    public required string TimeZoneId { get; init; }

    public required IReadOnlyList<AppointmentCalendarItem> Items { get; init; }
}

public sealed class AppointmentCalendarItem
{
    public required Guid AppointmentId { get; init; }

    public required DateTimeOffset ScheduledStartUtc { get; init; }

    public required DateTimeOffset ScheduledEndUtc { get; init; }

    public required DateTimeOffset ScheduledStartLocal { get; init; }

    public required DateTimeOffset ScheduledEndLocal { get; init; }

    public required AppointmentStatus Status { get; init; }

    public required string WorkshopLocationName { get; init; }

    public required string CustomerDisplayName { get; init; }

    public required string VehicleSummary { get; init; }
}

public sealed class AppointmentDetails
{
    public required Guid AppointmentId { get; init; }

    public required Guid WorkshopLocationId { get; init; }

    public required string WorkshopLocationName { get; init; }

    public required Guid CustomerId { get; init; }

    public required string CustomerDisplayName { get; init; }

    public required Guid VehicleId { get; init; }

    public required string VehicleSummary { get; init; }

    public required AppointmentStatus Status { get; init; }

    public required DateTimeOffset ScheduledStartUtc { get; init; }

    public required DateTimeOffset ScheduledEndUtc { get; init; }

    public required string TimeZoneId { get; init; }

    public string? CustomerConcern { get; init; }

    public string? InternalNotes { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    public Guid? LinkedRepairOrderId { get; init; }
}

public sealed class AppointmentOperationResult
{
    public bool Success { get; init; }

    public AppointmentOperationFailureReason? FailureReason { get; init; }

    public static AppointmentOperationResult Succeeded() => new() { Success = true };

    public static AppointmentOperationResult Failed(AppointmentOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public sealed class AppointmentOperationResult<T>
{
    public bool Success { get; init; }

    public T? Value { get; init; }

    public AppointmentOperationFailureReason? FailureReason { get; init; }

    public static AppointmentOperationResult<T> Succeeded(T value) =>
        new() { Success = true, Value = value };

    public static AppointmentOperationResult<T> Failed(AppointmentOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public enum AppointmentOperationFailureReason
{
    OrganizationUnresolved = 1,
    AppointmentNotFound = 2,
    InvalidInput = 3,
    WorkshopLocationNotFound = 4,
    CustomerNotFound = 5,
    VehicleNotFound = 6,
    CustomerVehicleMismatch = 7,
    PastStartNotAllowed = 8,
    VehicleOverlap = 9,
    ConcurrencyConflict = 10,
    AlreadyCancelled = 11,
    CannotModifyCancelled = 12,
}
