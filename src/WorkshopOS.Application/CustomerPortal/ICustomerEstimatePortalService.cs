using WorkshopOS.Domain.Estimates;

namespace WorkshopOS.Application.CustomerPortal;

public interface ICustomerEstimatePortalService
{
    Task<CustomerPortalSessionValidation?> ValidateSessionAsync(
        Guid sharePublicId,
        CancellationToken cancellationToken = default);

    Task<CustomerPortalOperationResult> ExchangeTokenAsync(
        ExchangeEstimateShareTokenCommand command,
        CancellationToken cancellationToken = default);

    Task<CustomerEstimatePortalDetails?> GetSharedEstimateAsync(
        Guid sharePublicId,
        CancellationToken cancellationToken = default);

    Task<CustomerPortalOperationResult> RecordApprovalAsync(
        Guid sharePublicId,
        CancellationToken cancellationToken = default);

    Task<CustomerPortalOperationResult> RecordDeclineAsync(
        Guid sharePublicId,
        CancellationToken cancellationToken = default);
}

public sealed record ExchangeEstimateShareTokenCommand
{
    public required Guid PublicId { get; init; }

    public required string RawToken { get; init; }
}

public sealed class CustomerPortalSessionValidation
{
    public required Guid SharePublicId { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid EstimateId { get; init; }
}

public sealed class CustomerEstimatePortalDetails
{
    public required string OrganizationDisplayName { get; init; }

    public required string EstimateNumber { get; init; }

    public required EstimateStatus Status { get; init; }

    public required string RepairOrderNumber { get; init; }

    public required string VehicleMake { get; init; }

    public required string VehicleModel { get; init; }

    public int? VehicleModelYear { get; init; }

    public string? VehicleRegistrationPlate { get; init; }

    public string? CustomerMessage { get; init; }

    public required string CurrencyCode { get; init; }

    public required IReadOnlyList<CustomerEstimatePortalItem> Items { get; init; }

    public required decimal Total { get; init; }

    public DateTimeOffset? PresentedAtUtc { get; init; }

    public DateTimeOffset? DecisionAtUtc { get; init; }

    public EstimateShareDecision? PortalDecision { get; init; }

    public bool CanApprove { get; init; }

    public bool CanDecline { get; init; }
}

public sealed class CustomerEstimatePortalItem
{
    public required string Description { get; init; }

    public required decimal Quantity { get; init; }

    public required decimal UnitPrice { get; init; }

    public required decimal LineTotal { get; init; }
}

public sealed class CustomerPortalOperationResult
{
    public bool Success { get; init; }

    public CustomerPortalOperationFailureReason? FailureReason { get; init; }

    public static CustomerPortalOperationResult Succeeded() => new() { Success = true };

    public static CustomerPortalOperationResult Failed(CustomerPortalOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public enum CustomerPortalOperationFailureReason
{
    InvalidAccess = 1,
    EstimateNotFound = 2,
    InvalidLifecycleTransition = 3,
    ConcurrencyConflict = 4,
}
