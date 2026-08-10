using Microsoft.EntityFrameworkCore;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Persistence;

namespace WorkshopOS.Infrastructure.Tenancy;

public sealed class OrganizationResolutionService
{
    private readonly AppDbContext _dbContext;

    public OrganizationResolutionService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Guid?> ResolveOrganizationIdAsync(
        Guid userId,
        Guid? selectedOrganizationId = null,
        CancellationToken cancellationToken = default)
    {
        var activeMembershipOrganizationIds = await (
            from membership in _dbContext.OrganizationMemberships.AsNoTracking()
            join organization in _dbContext.Organizations.AsNoTracking()
                on membership.OrganizationId equals organization.Id
            where membership.UserId == userId
                  && membership.Status == OrganizationMembershipStatus.Active
                  && organization.Status == OrganizationStatus.Active
            select membership.OrganizationId)
            .ToListAsync(cancellationToken);

        if (selectedOrganizationId.HasValue)
        {
            if (!activeMembershipOrganizationIds.Contains(selectedOrganizationId.Value))
            {
                return null;
            }

            return activeMembershipOrganizationIds.Count switch
            {
                0 => null,
                1 => activeMembershipOrganizationIds[0],
                _ => selectedOrganizationId,
            };
        }

        return activeMembershipOrganizationIds.Count switch
        {
            0 => null,
            1 => activeMembershipOrganizationIds[0],
            _ => null,
        };
    }
}
