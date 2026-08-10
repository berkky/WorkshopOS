using WorkshopOS.Domain.Staff;

namespace WorkshopOS.Application.Team;

public interface ITeamManagementService
{
    Task<TeamManagementResult<Guid>> CreateStaffMemberAsync(
        CreateStaffMemberCommand command,
        CancellationToken cancellationToken = default);

    Task<TeamManagementResult> UpdateStaffMemberAsync(
        UpdateStaffMemberCommand command,
        CancellationToken cancellationToken = default);

    Task<TeamManagementResult> DeactivateStaffMemberAsync(
        Guid staffMemberId,
        CancellationToken cancellationToken = default);

    Task<TeamManagementResult> SetStaffLocationsAsync(
        SetStaffLocationsCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TeamMemberListItem>> GetTeamListAsync(
        CancellationToken cancellationToken = default);

    Task<TeamMemberDetail?> GetStaffMemberAsync(
        Guid staffMemberId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LinkableMembershipOption>> GetLinkableMembershipsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TeamMembershipListItem>> GetMembershipAccessListAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkshopLocationOption>> GetWorkshopLocationOptionsAsync(
        CancellationToken cancellationToken = default);
}

public sealed record CreateStaffMemberCommand
{
    public required string DisplayName { get; init; }

    public required StaffPosition Position { get; init; }

    public string? JobTitle { get; init; }

    public string? ContactEmail { get; init; }

    public string? PhoneNumber { get; init; }

    public Guid? LinkedUserId { get; init; }

    public IReadOnlyList<Guid> WorkshopLocationIds { get; init; } = Array.Empty<Guid>();
}

public sealed record UpdateStaffMemberCommand
{
    public required Guid StaffMemberId { get; init; }

    public required string DisplayName { get; init; }

    public required StaffPosition Position { get; init; }

    public string? JobTitle { get; init; }

    public string? ContactEmail { get; init; }

    public string? PhoneNumber { get; init; }

    public Guid? LinkedUserId { get; init; }

    public StaffStatus Status { get; init; } = StaffStatus.Active;
}

public sealed record SetStaffLocationsCommand
{
    public required Guid StaffMemberId { get; init; }

    public required IReadOnlyList<Guid> WorkshopLocationIds { get; init; }
}

public sealed class TeamManagementResult
{
    public bool Success { get; init; }

    public TeamManagementFailureReason? FailureReason { get; init; }

    public static TeamManagementResult Succeeded() => new() { Success = true };

    public static TeamManagementResult Failed(TeamManagementFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public sealed class TeamManagementResult<T>
{
    public bool Success { get; init; }

    public T? Value { get; init; }

    public TeamManagementFailureReason? FailureReason { get; init; }

    public static TeamManagementResult<T> Succeeded(T value) =>
        new() { Success = true, Value = value };

    public static TeamManagementResult<T> Failed(TeamManagementFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public enum TeamManagementFailureReason
{
    OrganizationUnresolved = 1,
    StaffMemberNotFound = 2,
    LinkedUserNotMember = 3,
    LinkedUserAlreadyAssigned = 4,
    WorkshopLocationNotFound = 5,
    InvalidInput = 6,
}

public sealed class TeamMemberListItem
{
    public required Guid StaffMemberId { get; init; }

    public required string DisplayName { get; init; }

    public required string Position { get; init; }

    public required string Status { get; init; }

    public required string LoginAccess { get; init; }

    public required IReadOnlyList<string> WorkshopLocations { get; init; }
}

public sealed class TeamMemberDetail
{
    public required Guid StaffMemberId { get; init; }

    public required string DisplayName { get; init; }

    public required StaffPosition Position { get; init; }

    public string? JobTitle { get; init; }

    public string? ContactEmail { get; init; }

    public string? PhoneNumber { get; init; }

    public required StaffStatus Status { get; init; }

    public Guid? LinkedUserId { get; init; }

    public required IReadOnlyList<Guid> WorkshopLocationIds { get; init; }
}

public sealed class LinkableMembershipOption
{
    public required Guid UserId { get; init; }

    public required string DisplayLabel { get; init; }

    public required string MembershipRole { get; init; }
}

public sealed class TeamMembershipListItem
{
    public required Guid MembershipId { get; init; }

    public required Guid UserId { get; init; }

    public required string DisplayName { get; init; }

    public required string Role { get; init; }

    public required string Status { get; init; }
}

public sealed class WorkshopLocationOption
{
    public required Guid WorkshopLocationId { get; init; }

    public required string Name { get; init; }
}
