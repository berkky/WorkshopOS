using WorkshopOS.Domain.Estimates;

namespace WorkshopOS.Application.EstimateSharing;

public interface IEstimateSharingService
{
    Task<EstimateShareManagementDetails?> GetEstimateShareDetailsAsync(
        Guid estimateId,
        CancellationToken cancellationToken = default);

    Task<EstimateShareOperationResult<EstimateShareCreationResult>> CreateShareAsync(
        Guid actorUserId,
        CreateEstimateShareCommand command,
        CancellationToken cancellationToken = default);

    Task<EstimateShareOperationResult<EstimateShareCreationResult>> RotateShareAsync(
        Guid actorUserId,
        RotateEstimateShareCommand command,
        CancellationToken cancellationToken = default);

    Task<EstimateShareOperationResult> RevokeShareAsync(
        Guid actorUserId,
        RevokeEstimateShareCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record CreateEstimateShareCommand
{
    public required Guid EstimateId { get; init; }

    public int DurationDays { get; init; } = EstimateShareExpiryPolicy.DefaultDurationDays;
}

public sealed record RotateEstimateShareCommand
{
    public required Guid EstimateId { get; init; }

    public int DurationDays { get; init; } = EstimateShareExpiryPolicy.DefaultDurationDays;
}

public sealed record RevokeEstimateShareCommand
{
    public required Guid EstimateId { get; init; }
}

public sealed class EstimateShareManagementDetails
{
    public required Guid EstimateId { get; init; }

    public Guid? CurrentShareId { get; init; }

    public Guid? PublicId { get; init; }

    public EstimateShareStatus Status { get; init; }

    public DateTimeOffset? CreatedAtUtc { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public DateTimeOffset? RevokedAtUtc { get; init; }

    public DateTimeOffset? LastAccessedAtUtc { get; init; }

    public EstimateShareDecision? PortalDecision { get; init; }

    public DateTimeOffset? PortalDecisionAtUtc { get; init; }

    public bool CanCreate { get; init; }

    public bool CanRotate { get; init; }

    public bool CanRevoke { get; init; }
}

public enum EstimateShareStatus
{
    None = 0,
    Active = 1,
    Expired = 2,
    Revoked = 3,
}

public sealed class EstimateShareCreationResult
{
    public required Guid PublicId { get; init; }

    public required string RawToken { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }
}

public sealed class EstimateShareOperationResult
{
    public bool Success { get; init; }

    public EstimateShareOperationFailureReason? FailureReason { get; init; }

    public static EstimateShareOperationResult Succeeded() => new() { Success = true };

    public static EstimateShareOperationResult Failed(EstimateShareOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public sealed class EstimateShareOperationResult<T>
{
    public bool Success { get; init; }

    public T? Value { get; init; }

    public EstimateShareOperationFailureReason? FailureReason { get; init; }

    public static EstimateShareOperationResult<T> Succeeded(T value) =>
        new() { Success = true, Value = value };

    public static EstimateShareOperationResult<T> Failed(EstimateShareOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public enum EstimateShareOperationFailureReason
{
    OrganizationUnresolved = 1,
    EstimateNotFound = 2,
    Unauthorized = 3,
    InvalidInput = 4,
    EstimateNotEligible = 5,
    ActiveShareAlreadyExists = 6,
    ShareNotFound = 7,
    ConcurrencyConflict = 8,
}
