using WorkshopOS.Domain.Organizations;

namespace WorkshopOS.Application.Memberships;

public interface IOrganizationMembershipManagementService
{
    Task<MembershipManagementResult> ChangeRoleAsync(
        ChangeMembershipRoleCommand command,
        CancellationToken cancellationToken = default);

    Task<MembershipManagementResult> ChangeStatusAsync(
        ChangeMembershipStatusCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record ChangeMembershipRoleCommand
{
    public required Guid ActorUserId { get; init; }

    public required Guid MembershipId { get; init; }

    public required OrganizationMembershipRole NewRole { get; init; }
}

public sealed record ChangeMembershipStatusCommand
{
    public required Guid ActorUserId { get; init; }

    public required Guid MembershipId { get; init; }

    public required OrganizationMembershipStatus NewStatus { get; init; }
}

public sealed class MembershipManagementResult
{
    public bool Success { get; init; }

    public MembershipManagementFailureReason? FailureReason { get; init; }

    public static MembershipManagementResult Succeeded() => new() { Success = true };

    public static MembershipManagementResult Failed(MembershipManagementFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public enum MembershipManagementFailureReason
{
    OrganizationUnresolved = 1,
    MembershipNotFound = 2,
    ActorNotAuthorized = 3,
    SelfMutationRejected = 4,
    LastOwnerProtected = 5,
    InvalidRoleTransition = 6,
    ConcurrencyConflict = 7,
}
