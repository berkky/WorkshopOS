using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Organizations;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Persistence;

namespace WorkshopOS.Infrastructure.Organizations;

public sealed class OrganizationSelectionService : IOrganizationSelectionService
{
    private readonly AppDbContext _dbContext;

    public OrganizationSelectionService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<OrganizationSelectionOption>> GetSelectableOrganizationsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await (
            from membership in _dbContext.OrganizationMemberships.AsNoTracking()
            join organization in _dbContext.Organizations.AsNoTracking()
                on membership.OrganizationId equals organization.Id
            where membership.UserId == userId
                  && membership.Status == OrganizationMembershipStatus.Active
                  && organization.Status == OrganizationStatus.Active
            orderby organization.Name
            select new OrganizationSelectionOption
            {
                OrganizationId = organization.Id,
                OrganizationName = organization.Name,
                MembershipRole = membership.Role.ToString(),
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<OrganizationSelectionResult> SelectOrganizationAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var membership = await (
            from candidate in _dbContext.OrganizationMemberships.AsNoTracking()
            join organization in _dbContext.Organizations.AsNoTracking()
                on candidate.OrganizationId equals organization.Id
            where candidate.UserId == userId
                  && candidate.OrganizationId == organizationId
            select new
            {
                MembershipStatus = candidate.Status,
                OrganizationStatus = organization.Status,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (membership is null)
        {
            return OrganizationSelectionResult.Failed(OrganizationSelectionFailureReason.NotMember);
        }

        if (membership.MembershipStatus != OrganizationMembershipStatus.Active)
        {
            return OrganizationSelectionResult.Failed(OrganizationSelectionFailureReason.MembershipInactive);
        }

        if (membership.OrganizationStatus != OrganizationStatus.Active)
        {
            return OrganizationSelectionResult.Failed(OrganizationSelectionFailureReason.OrganizationInactive);
        }

        return OrganizationSelectionResult.Succeeded(organizationId);
    }
}
