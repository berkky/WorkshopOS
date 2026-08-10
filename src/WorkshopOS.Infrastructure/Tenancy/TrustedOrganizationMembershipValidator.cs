using Microsoft.EntityFrameworkCore;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Persistence;

namespace WorkshopOS.Infrastructure.Tenancy;

public sealed class TrustedOrganizationMembershipValidator
{
    private readonly AppDbContext _dbContext;

    public TrustedOrganizationMembershipValidator(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> HasActiveMembershipAsync(
        Guid userId,
        Guid organizationId,
        OrganizationMembershipRole? requiredRole = null,
        CancellationToken cancellationToken = default)
    {
        var query =
            from membership in _dbContext.OrganizationMemberships.AsNoTracking()
            join organization in _dbContext.Organizations.AsNoTracking()
                on membership.OrganizationId equals organization.Id
            where membership.UserId == userId
                  && membership.OrganizationId == organizationId
                  && membership.Status == OrganizationMembershipStatus.Active
                  && organization.Status == OrganizationStatus.Active
            select membership;

        if (requiredRole.HasValue)
        {
            query = query.Where(membership => membership.Role == requiredRole.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }
}
