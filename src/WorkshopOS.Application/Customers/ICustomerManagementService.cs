namespace WorkshopOS.Application.Customers;

public interface ICustomerManagementService
{
    Task<CustomerListResult> ListCustomersAsync(
        CustomerListQuery query,
        CancellationToken cancellationToken = default);

    Task<CustomerDetails?> GetCustomerDetailsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task<CustomerOperationResult<Guid>> CreateCustomerAsync(
        CreateCustomerCommand command,
        CancellationToken cancellationToken = default);

    Task<CustomerOperationResult> UpdateCustomerAsync(
        UpdateCustomerCommand command,
        CancellationToken cancellationToken = default);

    Task<CustomerOperationResult> DeactivateCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task<CustomerOperationResult> ActivateCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);
}

public sealed record CustomerListQuery
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;
}

public sealed record CreateCustomerCommand
{
    public required string DisplayName { get; init; }

    public string? Email { get; init; }

    public string? Phone { get; init; }

    public string? Notes { get; init; }
}

public sealed record UpdateCustomerCommand
{
    public required Guid CustomerId { get; init; }

    public required string DisplayName { get; init; }

    public string? Email { get; init; }

    public string? Phone { get; init; }

    public string? Notes { get; init; }

    public bool IsActive { get; init; } = true;
}

public sealed class CustomerListResult
{
    public required IReadOnlyList<CustomerListItem> Items { get; init; }

    public required int TotalCount { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }
}

public sealed class CustomerListItem
{
    public required Guid CustomerId { get; init; }

    public required string DisplayName { get; init; }

    public string? Email { get; init; }

    public string? Phone { get; init; }

    public required bool IsActive { get; init; }
}

public sealed class CustomerDetails
{
    public required Guid CustomerId { get; init; }

    public required string DisplayName { get; init; }

    public string? Email { get; init; }

    public string? Phone { get; init; }

    public string? Notes { get; init; }

    public required bool IsActive { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    public required int VehicleCount { get; init; }

    public required int UpcomingAppointmentCount { get; init; }
}

public sealed class CustomerOperationResult
{
    public bool Success { get; init; }

    public CustomerOperationFailureReason? FailureReason { get; init; }

    public static CustomerOperationResult Succeeded() => new() { Success = true };

    public static CustomerOperationResult Failed(CustomerOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public sealed class CustomerOperationResult<T>
{
    public bool Success { get; init; }

    public T? Value { get; init; }

    public CustomerOperationFailureReason? FailureReason { get; init; }

    public static CustomerOperationResult<T> Succeeded(T value) =>
        new() { Success = true, Value = value };

    public static CustomerOperationResult<T> Failed(CustomerOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public enum CustomerOperationFailureReason
{
    OrganizationUnresolved = 1,
    CustomerNotFound = 2,
    InvalidInput = 3,
}
