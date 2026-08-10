using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Team;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.Staff;
using WorkshopOS.Infrastructure.Identity;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.Team;

public sealed class TeamManagementService : ITeamManagementService
{
    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContext _organizationContext;

    public TeamManagementService(
        AppDbContext dbContext,
        IOrganizationContext organizationContext)
    {
        _dbContext = dbContext;
        _organizationContext = organizationContext;
    }

    public async Task<TeamManagementResult<Guid>> CreateStaffMemberAsync(
        CreateStaffMemberCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return TeamManagementResult<Guid>.Failed(TeamManagementFailureReason.OrganizationUnresolved);
        }

        if (string.IsNullOrWhiteSpace(command.DisplayName))
        {
            return TeamManagementResult<Guid>.Failed(TeamManagementFailureReason.InvalidInput);
        }

        if (command.LinkedUserId.HasValue)
        {
            var linkValidation = await ValidateLinkedUserAsync(
                organizationId,
                command.LinkedUserId.Value,
                excludeStaffMemberId: null,
                cancellationToken);

            if (linkValidation is not null)
            {
                return TeamManagementResult<Guid>.Failed(linkValidation.Value);
            }
        }

        var locationValidation = await ValidateWorkshopLocationsAsync(
            organizationId,
            command.WorkshopLocationIds,
            cancellationToken);

        if (locationValidation is not null)
        {
            return TeamManagementResult<Guid>.Failed(locationValidation.Value);
        }

        var staffMember = new StaffMember(
            organizationId,
            command.DisplayName,
            command.Position,
            command.JobTitle,
            command.ContactEmail,
            command.PhoneNumber,
            command.LinkedUserId);

        _dbContext.StaffMembers.Add(staffMember);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await ReplaceStaffLocationsAsync(
            organizationId,
            staffMember.Id,
            command.WorkshopLocationIds,
            cancellationToken);

        return TeamManagementResult<Guid>.Succeeded(staffMember.Id);
    }

    public async Task<TeamManagementResult> UpdateStaffMemberAsync(
        UpdateStaffMemberCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return TeamManagementResult.Failed(TeamManagementFailureReason.OrganizationUnresolved);
        }

        var staffMember = await _dbContext.StaffMembers
            .SingleOrDefaultAsync(member => member.Id == command.StaffMemberId, cancellationToken);

        if (staffMember is null)
        {
            return TeamManagementResult.Failed(TeamManagementFailureReason.StaffMemberNotFound);
        }

        if (command.LinkedUserId.HasValue)
        {
            var linkValidation = await ValidateLinkedUserAsync(
                organizationId,
                command.LinkedUserId.Value,
                command.StaffMemberId,
                cancellationToken);

            if (linkValidation is not null)
            {
                return TeamManagementResult.Failed(linkValidation.Value);
            }
        }

        staffMember.UpdateProfile(
            command.DisplayName,
            command.Position,
            command.JobTitle,
            command.ContactEmail,
            command.PhoneNumber,
            command.LinkedUserId);

        if (command.Status == StaffStatus.Active)
        {
            staffMember.Activate();
        }
        else
        {
            staffMember.Deactivate();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return TeamManagementResult.Succeeded();
    }

    public async Task<TeamManagementResult> DeactivateStaffMemberAsync(
        Guid staffMemberId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return TeamManagementResult.Failed(TeamManagementFailureReason.OrganizationUnresolved);
        }

        var staffMember = await _dbContext.StaffMembers
            .SingleOrDefaultAsync(member => member.Id == staffMemberId, cancellationToken);

        if (staffMember is null)
        {
            return TeamManagementResult.Failed(TeamManagementFailureReason.StaffMemberNotFound);
        }

        staffMember.Deactivate();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return TeamManagementResult.Succeeded();
    }

    public async Task<TeamManagementResult> SetStaffLocationsAsync(
        SetStaffLocationsCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return TeamManagementResult.Failed(TeamManagementFailureReason.OrganizationUnresolved);
        }

        var staffMember = await _dbContext.StaffMembers
            .SingleOrDefaultAsync(member => member.Id == command.StaffMemberId, cancellationToken);

        if (staffMember is null)
        {
            return TeamManagementResult.Failed(TeamManagementFailureReason.StaffMemberNotFound);
        }

        var locationValidation = await ValidateWorkshopLocationsAsync(
            organizationId,
            command.WorkshopLocationIds,
            cancellationToken);

        if (locationValidation is not null)
        {
            return TeamManagementResult.Failed(locationValidation.Value);
        }

        await ReplaceStaffLocationsAsync(
            organizationId,
            command.StaffMemberId,
            command.WorkshopLocationIds,
            cancellationToken);

        return TeamManagementResult.Succeeded();
    }

    public async Task<IReadOnlyList<TeamMemberListItem>> GetTeamListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return Array.Empty<TeamMemberListItem>();
        }

        var staffMembers = await _dbContext.StaffMembers.AsNoTracking()
            .Where(member => member.OrganizationId == organizationId)
            .OrderBy(member => member.DisplayName)
            .ToListAsync(cancellationToken);

        var assignments = await _dbContext.StaffLocationAssignments.AsNoTracking()
            .Where(assignment => assignment.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);

        var locations = await _dbContext.WorkshopLocations.AsNoTracking()
            .Where(location => location.OrganizationId == organizationId)
            .ToDictionaryAsync(location => location.Id, cancellationToken);

        var memberships = await (
            from membership in _dbContext.OrganizationMemberships.AsNoTracking()
            join user in _dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
            where membership.OrganizationId == organizationId
            select new
            {
                membership.UserId,
                membership.Role,
                membership.Status,
                user.DisplayName,
            })
            .ToDictionaryAsync(item => item.UserId, cancellationToken);

        return staffMembers
            .Select(member =>
            {
                var memberLocations = assignments
                    .Where(assignment => assignment.StaffMemberId == member.Id)
                    .Select(assignment => locations.TryGetValue(assignment.WorkshopLocationId, out var location)
                        ? location.Name
                        : "Unknown")
                    .ToList();

                var loginAccess = "Not linked";
                if (member.UserId.HasValue
                    && memberships.TryGetValue(member.UserId.Value, out var membership))
                {
                    loginAccess = $"{membership.Role} / {membership.Status}";
                }

                return new TeamMemberListItem
                {
                    StaffMemberId = member.Id,
                    DisplayName = member.DisplayName,
                    Position = member.Position.ToString(),
                    Status = member.Status.ToString(),
                    LoginAccess = loginAccess,
                    WorkshopLocations = memberLocations,
                };
            })
            .ToList();
    }

    public async Task<TeamMemberDetail?> GetStaffMemberAsync(
        Guid staffMemberId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return null;
        }

        var staffMember = await _dbContext.StaffMembers.AsNoTracking()
            .SingleOrDefaultAsync(member => member.Id == staffMemberId, cancellationToken);

        if (staffMember is null)
        {
            return null;
        }

        var locationIds = await _dbContext.StaffLocationAssignments.AsNoTracking()
            .Where(assignment => assignment.StaffMemberId == staffMemberId)
            .Select(assignment => assignment.WorkshopLocationId)
            .ToListAsync(cancellationToken);

        return new TeamMemberDetail
        {
            StaffMemberId = staffMember.Id,
            DisplayName = staffMember.DisplayName,
            Position = staffMember.Position,
            JobTitle = staffMember.JobTitle,
            ContactEmail = staffMember.ContactEmail,
            PhoneNumber = staffMember.PhoneNumber,
            Status = staffMember.Status,
            LinkedUserId = staffMember.UserId,
            WorkshopLocationIds = locationIds,
        };
    }

    public async Task<IReadOnlyList<LinkableMembershipOption>> GetLinkableMembershipsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return Array.Empty<LinkableMembershipOption>();
        }

        return await (
            from membership in _dbContext.OrganizationMemberships.AsNoTracking()
            join user in _dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
            where membership.OrganizationId == organizationId
                  && membership.Status == OrganizationMembershipStatus.Active
            orderby user.DisplayName
            select new LinkableMembershipOption
            {
                UserId = membership.UserId,
                DisplayLabel = user.DisplayName ?? user.Email ?? membership.UserId.ToString(),
                MembershipRole = membership.Role.ToString(),
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TeamMembershipListItem>> GetMembershipAccessListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return Array.Empty<TeamMembershipListItem>();
        }

        return await (
            from membership in _dbContext.OrganizationMemberships.AsNoTracking()
            join user in _dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
            where membership.OrganizationId == organizationId
            orderby user.DisplayName
            select new TeamMembershipListItem
            {
                MembershipId = membership.Id,
                UserId = membership.UserId,
                DisplayName = user.DisplayName ?? user.Email ?? membership.UserId.ToString(),
                Role = membership.Role.ToString(),
                Status = membership.Status.ToString(),
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkshopLocationOption>> GetWorkshopLocationOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return Array.Empty<WorkshopLocationOption>();
        }

        return await _dbContext.WorkshopLocations.AsNoTracking()
            .Where(location => location.OrganizationId == organizationId && location.IsActive)
            .OrderBy(location => location.Name)
            .Select(location => new WorkshopLocationOption
            {
                WorkshopLocationId = location.Id,
                Name = location.Name,
            })
            .ToListAsync(cancellationToken);
    }

    private bool TryGetOrganizationId(out Guid organizationId)
    {
        if (_organizationContext.IsResolved && _organizationContext.OrganizationId.HasValue)
        {
            organizationId = _organizationContext.OrganizationId.Value;
            return true;
        }

        organizationId = Guid.Empty;
        return false;
    }

    private async Task<TeamManagementFailureReason?> ValidateLinkedUserAsync(
        Guid organizationId,
        Guid linkedUserId,
        Guid? excludeStaffMemberId,
        CancellationToken cancellationToken)
    {
        var hasMembership = await _dbContext.OrganizationMemberships.AsNoTracking()
            .AnyAsync(
                membership => membership.OrganizationId == organizationId
                              && membership.UserId == linkedUserId,
                cancellationToken);

        if (!hasMembership)
        {
            return TeamManagementFailureReason.LinkedUserNotMember;
        }

        var alreadyLinked = await _dbContext.StaffMembers.AsNoTracking()
            .AnyAsync(
                member => member.OrganizationId == organizationId
                          && member.UserId == linkedUserId
                          && (!excludeStaffMemberId.HasValue || member.Id != excludeStaffMemberId.Value),
                cancellationToken);

        if (alreadyLinked)
        {
            return TeamManagementFailureReason.LinkedUserAlreadyAssigned;
        }

        return null;
    }

    private async Task<TeamManagementFailureReason?> ValidateWorkshopLocationsAsync(
        Guid organizationId,
        IReadOnlyList<Guid> workshopLocationIds,
        CancellationToken cancellationToken)
    {
        if (workshopLocationIds.Count == 0)
        {
            return null;
        }

        var distinctIds = workshopLocationIds.Distinct().ToList();
        var existingCount = await _dbContext.WorkshopLocations.AsNoTracking()
            .CountAsync(
                location => location.OrganizationId == organizationId
                            && distinctIds.Contains(location.Id),
                cancellationToken);

        return existingCount == distinctIds.Count
            ? null
            : TeamManagementFailureReason.WorkshopLocationNotFound;
    }

    private async Task ReplaceStaffLocationsAsync(
        Guid organizationId,
        Guid staffMemberId,
        IReadOnlyList<Guid> workshopLocationIds,
        CancellationToken cancellationToken)
    {
        var existingAssignments = await _dbContext.StaffLocationAssignments
            .Where(assignment => assignment.StaffMemberId == staffMemberId)
            .ToListAsync(cancellationToken);

        _dbContext.StaffLocationAssignments.RemoveRange(existingAssignments);

        foreach (var locationId in workshopLocationIds.Distinct())
        {
            _dbContext.StaffLocationAssignments.Add(
                new StaffLocationAssignment(organizationId, staffMemberId, locationId));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
