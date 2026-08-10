using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Onboarding;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Infrastructure.Identity;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;
using WorkshopOS.Web.Models.Onboarding;

namespace WorkshopOS.Web.Controllers;

[Route("onboarding")]
public sealed class OnboardingController : Controller
{
    private readonly IOwnerOnboardingService _ownerOnboardingService;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly OrganizationResolutionService _organizationResolutionService;
    private readonly AppDbContext _dbContext;

    public OnboardingController(
        IOwnerOnboardingService ownerOnboardingService,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        OrganizationResolutionService organizationResolutionService,
        AppDbContext dbContext)
    {
        _ownerOnboardingService = ownerOnboardingService;
        _signInManager = signInManager;
        _userManager = userManager;
        _organizationResolutionService = organizationResolutionService;
        _dbContext = dbContext;
    }

    [AllowAnonymous]
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var redirect = await GetAuthenticatedOnboardingRedirectAsync();
            if (redirect is not null)
            {
                return redirect;
            }

            return Forbid();
        }

        return View(new OwnerOnboardingViewModel());
    }

    [AllowAnonymous]
    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("OwnerOnboarding")]
    public async Task<IActionResult> Index(OwnerOnboardingViewModel model)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var redirect = await GetAuthenticatedOnboardingRedirectAsync();
            return redirect ?? Forbid();
        }

        if (!ModelState.IsValid)
        {
            model.Password = string.Empty;
            model.ConfirmPassword = string.Empty;
            return View(model);
        }

        var result = await _ownerOnboardingService.OnboardOwnerAsync(new OwnerOnboardingCommand
        {
            OwnerDisplayName = model.OwnerDisplayName,
            Email = model.Email,
            Password = model.Password,
            OrganizationName = model.OrganizationName,
            WorkshopLocationName = model.WorkshopLocationName,
            WorkshopLocationCode = model.WorkshopLocationCode,
            TimeZoneId = model.TimeZoneId,
            CurrencyCode = model.CurrencyCode,
        });

        if (!result.Success)
        {
            model.Password = string.Empty;
            model.ConfirmPassword = string.Empty;
            ModelState.AddModelError(
                string.Empty,
                result.FailureCategory == OwnerOnboardingFailureCategory.InvalidInput
                    ? "Lütfen girdiğiniz bilgileri kontrol edin."
                    : "Bu bilgilerle hesap oluşturulamadı. Bilgileri kontrol edip tekrar deneyin.");
            return View(model);
        }

        var user = await _userManager.FindByIdAsync(result.UserId.ToString());
        if (user is null)
        {
            model.Password = string.Empty;
            model.ConfirmPassword = string.Empty;
            ModelState.AddModelError(string.Empty, "Bu bilgilerle hesap oluşturulamadı. Bilgileri kontrol edip tekrar deneyin.");
            return View(model);
        }

        await _signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToAction(nameof(Complete));
    }

    [Authorize(Policy = PolicyNames.OrganizationMember)]
    [HttpGet("complete")]
    public async Task<IActionResult> Complete()
    {
        var organizationContext = HttpContext.RequestServices.GetRequiredService<IOrganizationContext>();
        if (!organizationContext.IsResolved || !organizationContext.OrganizationId.HasValue)
        {
            return Forbid();
        }

        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Forbid();
        }

        var organizationId = organizationContext.OrganizationId.Value;

        var viewModel = await (
            from organization in _dbContext.Organizations.AsNoTracking()
            join membership in _dbContext.OrganizationMemberships.AsNoTracking()
                on organization.Id equals membership.OrganizationId
            join location in _dbContext.WorkshopLocations.AsNoTracking()
                on organization.Id equals location.OrganizationId
            where organization.Id == organizationId
                  && membership.UserId == userId
                  && membership.Status == OrganizationMembershipStatus.Active
                  && organization.Status == OrganizationStatus.Active
            orderby location.CreatedAtUtc
            select new OnboardingCompleteViewModel
            {
                OrganizationName = organization.Name,
                WorkshopLocationName = location.Name,
                MembershipRole = membership.Role.ToString(),
            })
            .FirstOrDefaultAsync();

        if (viewModel is null)
        {
            return Forbid();
        }

        return View(viewModel);
    }

    private async Task<IActionResult?> GetAuthenticatedOnboardingRedirectAsync()
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Forbid();
        }

        Guid? selectedOrganizationId = null;
        var selectedOrganizationValue = User.FindFirstValue(WorkshopOsClaimTypes.SelectedOrganization);
        if (Guid.TryParse(selectedOrganizationValue, out var parsedSelectedOrganizationId))
        {
            selectedOrganizationId = parsedSelectedOrganizationId;
        }

        var resolvedOrganizationId = await _organizationResolutionService.ResolveOrganizationIdAsync(
            userId,
            selectedOrganizationId);

        if (resolvedOrganizationId.HasValue)
        {
            return RedirectToAction(nameof(Complete));
        }

        var activeMembershipCount = await (
            from membership in _dbContext.OrganizationMemberships.AsNoTracking()
            join organization in _dbContext.Organizations.AsNoTracking()
                on membership.OrganizationId equals organization.Id
            where membership.UserId == userId
                  && membership.Status == OrganizationMembershipStatus.Active
                  && organization.Status == OrganizationStatus.Active
            select membership.Id)
            .CountAsync();

        return activeMembershipCount >= 2
            ? RedirectToAction("Select", "Organization")
            : null;
    }
}
