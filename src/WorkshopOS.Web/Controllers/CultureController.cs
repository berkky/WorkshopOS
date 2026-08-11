using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using WorkshopOS.Web.Localization;

namespace WorkshopOS.Web.Controllers;

[Route("culture")]
public sealed class CultureController : Controller
{
    [HttpPost("set")]
    [ValidateAntiForgeryToken]
    public IActionResult SetCulture(string culture, string? returnUrl)
    {
        var normalizedCulture = WorkshopCultures.NormalizeOrDefault(culture);
        var safeReturnUrl = LocalRedirectHelper.ResolveReturnUrl(Url, returnUrl);

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(normalizedCulture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps,
                Path = "/",
            });

        return LocalRedirect(safeReturnUrl);
    }
}
