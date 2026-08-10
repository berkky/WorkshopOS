namespace WorkshopOS.Application.Vehicles;

public interface IVehicleManagementService
{
    Task<VehicleListResult> ListVehiclesAsync(
        VehicleListQuery query,
        CancellationToken cancellationToken = default);

    Task<VehicleDetails?> GetVehicleDetailsAsync(
        Guid vehicleId,
        CancellationToken cancellationToken = default);

    Task<VehicleOperationResult<Guid>> CreateVehicleAsync(
        CreateVehicleCommand command,
        CancellationToken cancellationToken = default);

    Task<VehicleOperationResult> UpdateVehicleAsync(
        UpdateVehicleCommand command,
        CancellationToken cancellationToken = default);

    Task<VehicleOperationResult> ReassignVehicleCustomerAsync(
        ReassignVehicleCustomerCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record VehicleListQuery
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    public string? Search { get; init; }

    public Guid? CurrentCustomerId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;
}

public sealed record CreateVehicleCommand
{
    public required string Make { get; init; }

    public required string Model { get; init; }

    public int? ModelYear { get; init; }

    public string? Vin { get; init; }

    public string? RegistrationPlate { get; init; }

    public string? Color { get; init; }

    public Guid? CurrentCustomerId { get; init; }
}

public sealed record UpdateVehicleCommand
{
    public required Guid VehicleId { get; init; }

    public required string Make { get; init; }

    public required string Model { get; init; }

    public int? ModelYear { get; init; }

    public string? Vin { get; init; }

    public string? RegistrationPlate { get; init; }

    public string? Color { get; init; }
}

public sealed record ReassignVehicleCustomerCommand
{
    public required Guid VehicleId { get; init; }

    public Guid? NewCurrentCustomerId { get; init; }
}

public sealed class VehicleListResult
{
    public required IReadOnlyList<VehicleListItem> Items { get; init; }

    public required int TotalCount { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }
}

public sealed class VehicleListItem
{
    public required Guid VehicleId { get; init; }

    public required string Make { get; init; }

    public required string Model { get; init; }

    public int? ModelYear { get; init; }

    public string? Vin { get; init; }

    public string? RegistrationPlate { get; init; }

    public string? CurrentCustomerDisplayName { get; init; }
}

public sealed class VehicleDetails
{
    public required Guid VehicleId { get; init; }

    public required string Make { get; init; }

    public required string Model { get; init; }

    public int? ModelYear { get; init; }

    public string? Vin { get; init; }

    public string? RegistrationPlate { get; init; }

    public string? Color { get; init; }

    public Guid? CurrentCustomerId { get; init; }

    public string? CurrentCustomerDisplayName { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    public required int RepairOrderCount { get; init; }

    public DateTimeOffset? LastRepairOrderOpenedAtUtc { get; init; }

    public required int UpcomingAppointmentCount { get; init; }

    public DateTimeOffset? NextAppointmentStartUtc { get; init; }
}

public sealed class VehicleOperationResult
{
    public bool Success { get; init; }

    public VehicleOperationFailureReason? FailureReason { get; init; }

    public static VehicleOperationResult Succeeded() => new() { Success = true };

    public static VehicleOperationResult Failed(VehicleOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public sealed class VehicleOperationResult<T>
{
    public bool Success { get; init; }

    public T? Value { get; init; }

    public VehicleOperationFailureReason? FailureReason { get; init; }

    public static VehicleOperationResult<T> Succeeded(T value) =>
        new() { Success = true, Value = value };

    public static VehicleOperationResult<T> Failed(VehicleOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public enum VehicleOperationFailureReason
{
    OrganizationUnresolved = 1,
    VehicleNotFound = 2,
    CustomerNotFound = 3,
    InvalidInput = 4,
}
