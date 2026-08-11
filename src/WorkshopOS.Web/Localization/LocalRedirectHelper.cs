using Microsoft.AspNetCore.Mvc;

namespace WorkshopOS.Web.Localization;

public static class LocalRedirectHelper
{
    public static string ResolveReturnUrl(IUrlHelper urlHelper, string? returnUrl, string fallbackPath = "/")
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && urlHelper.IsLocalUrl(returnUrl))
        {
            return returnUrl;
        }

        return fallbackPath;
    }
}
