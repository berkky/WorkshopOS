using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkshopOS.Application.CustomerPortal;
using WorkshopOS.Web.Authentication;
using WorkshopOS.Web.Models.CustomerPortal;

namespace WorkshopOS.Web.Controllers;

public sealed class PortalController : Controller
{
    private readonly ICustomerEstimatePortalService _portalService;

    public PortalController(ICustomerEstimatePortalService portalService)
    {
        _portalService = portalService;
    }

    [AllowAnonymous]
    [HttpGet("/portal/access/{publicId:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Access(Guid publicId)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
        Response.Headers["X-Frame-Options"] = "DENY";

        return View(new PortalAccessBootstrapViewModel { PublicId = publicId });
    }

    [AllowAnonymous]
    [EnableRateLimiting("CustomerPortalAccess")]
    [HttpPost("/portal/access/{publicId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Exchange(Guid publicId, string token)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
        Response.Headers["X-Frame-Options"] = "DENY";

        var result = await _portalService.ExchangeTokenAsync(new ExchangeEstimateShareTokenCommand
        {
            PublicId = publicId,
            RawToken = token,
        });

        if (!result.Success)
        {
            return View("AccessUnavailable");
        }

        var claims = new List<Claim>
        {
            new(CustomerPortalClaimTypes.SharePublicId, publicId.ToString()),
            new(CustomerPortalClaimTypes.SessionKind, "estimate_share"),
        };

        var identity = new ClaimsIdentity(claims, CustomerPortalAuthDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CustomerPortalAuthDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2),
                AllowRefresh = false,
            });

        return RedirectToAction(nameof(Estimate));
    }

    [Authorize(AuthenticationSchemes = CustomerPortalAuthDefaults.AuthenticationScheme)]
    [HttpGet("/portal/estimate")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Estimate()
    {
        ApplyPortalSecurityHeaders();

        if (!TryGetSharePublicId(out var publicId))
        {
            return Challenge(CustomerPortalAuthDefaults.AuthenticationScheme);
        }

        var details = await _portalService.GetSharedEstimateAsync(publicId);
        if (details is null)
        {
            return View("AccessUnavailable");
        }

        return View(MapDetails(details));
    }

    [Authorize(AuthenticationSchemes = CustomerPortalAuthDefaults.AuthenticationScheme)]
    [HttpPost("/portal/estimate/approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve()
    {
        ApplyPortalSecurityHeaders();

        if (!TryGetSharePublicId(out var publicId))
        {
            return Challenge(CustomerPortalAuthDefaults.AuthenticationScheme);
        }

        var result = await _portalService.RecordApprovalAsync(publicId);
        if (!result.Success)
        {
            TempData["PortalError"] = MapFailure(result.FailureReason);
        }

        return RedirectToAction(nameof(Estimate));
    }

    [Authorize(AuthenticationSchemes = CustomerPortalAuthDefaults.AuthenticationScheme)]
    [HttpPost("/portal/estimate/decline")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decline()
    {
        ApplyPortalSecurityHeaders();

        if (!TryGetSharePublicId(out var publicId))
        {
            return Challenge(CustomerPortalAuthDefaults.AuthenticationScheme);
        }

        var result = await _portalService.RecordDeclineAsync(publicId);
        if (!result.Success)
        {
            TempData["PortalError"] = MapFailure(result.FailureReason);
        }

        return RedirectToAction(nameof(Estimate));
    }

    [Authorize(AuthenticationSchemes = CustomerPortalAuthDefaults.AuthenticationScheme)]
    [HttpPost("/portal/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CustomerPortalAuthDefaults.AuthenticationScheme);
        return View("AccessUnavailable");
    }

    [AllowAnonymous]
    [HttpGet("/portal/access/unavailable")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult AccessUnavailable()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return View();
    }

    private bool TryGetSharePublicId(out Guid publicId)
    {
        var value = User.FindFirstValue(CustomerPortalClaimTypes.SharePublicId);
        return Guid.TryParse(value, out publicId);
    }

    private void ApplyPortalSecurityHeaders()
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
        Response.Headers["X-Frame-Options"] = "DENY";
    }

    private static CustomerPortalEstimateViewModel MapDetails(CustomerEstimatePortalDetails details) =>
        new()
        {
            OrganizationDisplayName = details.OrganizationDisplayName,
            EstimateNumber = details.EstimateNumber,
            Status = details.Status,
            RepairOrderNumber = details.RepairOrderNumber,
            VehicleMake = details.VehicleMake,
            VehicleModel = details.VehicleModel,
            VehicleModelYear = details.VehicleModelYear,
            VehicleRegistrationPlate = details.VehicleRegistrationPlate,
            CustomerMessage = details.CustomerMessage,
            CurrencyCode = details.CurrencyCode,
            Total = details.Total,
            PresentedAtUtc = details.PresentedAtUtc,
            DecisionAtUtc = details.DecisionAtUtc,
            PortalDecision = details.PortalDecision,
            CanApprove = details.CanApprove,
            CanDecline = details.CanDecline,
            Items = details.Items
                .Select(item => new CustomerPortalEstimateItemViewModel
                {
                    Description = item.Description,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    LineTotal = item.LineTotal,
                })
                .ToList(),
        };

    private static string MapFailure(CustomerPortalOperationFailureReason? reason) =>
        reason switch
        {
            CustomerPortalOperationFailureReason.InvalidLifecycleTransition =>
                "This estimate can no longer be approved or declined.",
            CustomerPortalOperationFailureReason.ConcurrencyConflict =>
                "Your decision could not be recorded because the estimate was updated. Refresh and try again.",
            _ => "This secure link is invalid or no longer available.",
        };
}
