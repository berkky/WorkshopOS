using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WorkshopOS.Application.Organizations;
using WorkshopOS.Infrastructure.Identity;
using WorkshopOS.Web.Models.Organizations;

namespace WorkshopOS.Web.Controllers;

[Authorize]
[Route("organization")]
public sealed class OrganizationController : Controller
{
    private readonly IOrganizationSelectionService _organizationSelectionService;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public OrganizationController(
        IOrganizationSelectionService organizationSelectionService,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        _organizationSelectionService = organizationSelectionService;
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [HttpGet("select")]
    public async Task<IActionResult> Select(string? returnUrl = null)
    {
        var userId = await GetCurrentUserIdAsync();
        if (userId is null)
        {
            return Challenge();
        }

        var options = await _organizationSelectionService.GetSelectableOrganizationsAsync(userId.Value);
        if (options.Count <= 1)
        {
            return RedirectToLocal(returnUrl) ?? RedirectToAction("Complete", "Onboarding");
        }

        return View(new OrganizationSelectionViewModel
        {
            ReturnUrl = returnUrl,
            Organizations = options
                .Select(option => new OrganizationSelectionItemViewModel
                {
                    OrganizationId = option.OrganizationId,
                    OrganizationName = option.OrganizationName,
                    MembershipRole = option.MembershipRole,
                })
                .ToList(),
        });
    }

    [HttpPost("select")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Select(OrganizationSelectPostViewModel model)
    {
        var userId = await GetCurrentUserIdAsync();
        if (userId is null)
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            return await Select(model.ReturnUrl);
        }

        var selectionResult = await _organizationSelectionService.SelectOrganizationAsync(
            userId.Value,
            model.OrganizationId);

        if (!selectionResult.Success)
        {
            return Forbid();
        }

        var user = await _userManager.FindByIdAsync(userId.Value.ToString());
        if (user is null)
        {
            return Forbid();
        }

        await _signInManager.SignOutAsync();

        var additionalClaims = new[]
        {
            new Claim(WorkshopOsClaimTypes.SelectedOrganization, selectionResult.OrganizationId.ToString()),
        };

        await _signInManager.SignInWithClaimsAsync(user, new AuthenticationProperties
        {
            IsPersistent = false,
        }, additionalClaims);

        return RedirectToLocal(model.ReturnUrl) ?? RedirectToAction("Complete", "Onboarding");
    }

    private async Task<Guid?> GetCurrentUserIdAsync()
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdValue, out var userId) ? userId : null;
    }

    private IActionResult? RedirectToLocal(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return null;
    }
}
