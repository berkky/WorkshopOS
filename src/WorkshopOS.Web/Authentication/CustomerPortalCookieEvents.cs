using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using WorkshopOS.Application.CustomerPortal;
using WorkshopOS.Infrastructure.Tenancy;
using WorkshopOS.Web.Authentication;

namespace WorkshopOS.Web.Authentication;

public sealed class CustomerPortalCookieEvents : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var publicIdValue = context.Principal?.FindFirst(CustomerPortalClaimTypes.SharePublicId)?.Value;
        if (!Guid.TryParse(publicIdValue, out var publicId))
        {
            await RejectAsync(context);
            return;
        }

        var portalService = context.HttpContext.RequestServices
            .GetRequiredService<ICustomerEstimatePortalService>();

        var validation = await portalService.ValidateSessionAsync(
            publicId,
            context.HttpContext.RequestAborted);

        if (validation is null)
        {
            await RejectAsync(context);
            return;
        }

        var organizationContextMutator = context.HttpContext.RequestServices
            .GetRequiredService<IOrganizationContextMutator>();
        organizationContextMutator.Resolve(validation.OrganizationId);
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CustomerPortalAuthDefaults.AuthenticationScheme);
    }
}
