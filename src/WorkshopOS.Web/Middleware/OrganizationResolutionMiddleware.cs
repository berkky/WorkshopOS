using System.Security.Claims;
using WorkshopOS.Infrastructure.Identity;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Web.Middleware;

public sealed class OrganizationResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public OrganizationResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext httpContext,
        OrganizationResolutionService resolutionService,
        IOrganizationContextMutator organizationContextMutator)
    {
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            var userIdValue = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userIdValue, out var userId))
            {
                Guid? selectedOrganizationId = null;
                var selectedOrganizationValue = httpContext.User.FindFirstValue(WorkshopOsClaimTypes.SelectedOrganization);
                if (Guid.TryParse(selectedOrganizationValue, out var parsedSelectedOrganizationId))
                {
                    selectedOrganizationId = parsedSelectedOrganizationId;
                }

                var organizationId = await resolutionService.ResolveOrganizationIdAsync(
                    userId,
                    selectedOrganizationId,
                    httpContext.RequestAborted);

                if (organizationId.HasValue)
                {
                    organizationContextMutator.Resolve(organizationId.Value);
                }
            }
        }

        await _next(httpContext);
    }
}
