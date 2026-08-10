using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.Authorization;

public sealed class OrganizationMemberAuthorizationHandler
    : AuthorizationHandler<OrganizationMemberRequirement>
{
    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContext _organizationContext;

    public OrganizationMemberAuthorizationHandler(
        AppDbContext dbContext,
        IOrganizationContext organizationContext)
    {
        _dbContext = dbContext;
        _organizationContext = organizationContext;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OrganizationMemberRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true
            || !_organizationContext.IsResolved
            || !_organizationContext.OrganizationId.HasValue)
        {
            return;
        }

        var userIdValue = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return;
        }

        var organizationId = _organizationContext.OrganizationId.Value;

        var hasActiveMembership = await (
            from membership in _dbContext.OrganizationMemberships.AsNoTracking()
            join organization in _dbContext.Organizations.AsNoTracking()
                on membership.OrganizationId equals organization.Id
            where membership.UserId == userId
                  && membership.OrganizationId == organizationId
                  && membership.Status == OrganizationMembershipStatus.Active
                  && organization.Status == OrganizationStatus.Active
            select membership.Id)
            .AnyAsync();

        if (hasActiveMembership)
        {
            context.Succeed(requirement);
        }
    }
}
