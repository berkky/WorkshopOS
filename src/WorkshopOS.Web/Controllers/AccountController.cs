using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using WorkshopOS.Infrastructure.Identity;
using WorkshopOS.Web.Localization;
using WorkshopOS.Web.Models.Account;
using WorkshopOS.Web.Services;

namespace WorkshopOS.Web.Controllers;

[Route("account")]
public sealed class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebFailureMessages _messages;
    private readonly IAccountCenterService _accountCenter;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IWebFailureMessages messages,
        IAccountCenterService accountCenter,
        IStringLocalizer<SharedResource> localizer)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _messages = messages;
        _accountCenter = accountCenter;
        _localizer = localizer;
    }

    [AllowAnonymous]
    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToStaffHome(returnUrl);
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("StaffLogin")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, _messages.LoginInvalidCredentials());
            return View(model);
        }

        var signInResult = await _signInManager.PasswordSignInAsync(
            user,
            model.Password,
            model.RememberMe,
            lockoutOnFailure: true);

        if (!signInResult.Succeeded)
        {
            ModelState.AddModelError(string.Empty, _messages.LoginInvalidCredentials());
            model.Password = string.Empty;
            return View(model);
        }

        return RedirectToStaffHome(model.ReturnUrl);
    }

    [Authorize]
    [HttpGet("profile")]
    public async Task<IActionResult> Profile()
    {
        var snapshot = await _accountCenter.GetSnapshotAsync(User);
        if (snapshot is null)
        {
            return Challenge();
        }

        return View(MapProfile(snapshot));
    }

    [Authorize]
    [HttpPost("profile")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            var snapshot = await _accountCenter.GetSnapshotAsync(User);
            if (snapshot is not null)
            {
                model.Email = snapshot.Email;
                model.DisplayName = snapshot.DisplayName;
                model.Initials = snapshot.Initials;
                model.OrganizationName = snapshot.OrganizationName;
                model.MembershipRoleLabel = snapshot.MembershipRoleLabel;
                model.MembershipStatusLabel = snapshot.MembershipStatusLabel;
                model.OrganizationTimeZoneId = snapshot.OrganizationTimeZoneId;
            }

            return View(model);
        }

        await _userManager.SetPhoneNumberAsync(user, string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim());
        TempData["Success"] = _localizer["Account_PhoneUpdated"].Value;
        return RedirectToAction(nameof(Profile));
    }

    [Authorize]
    [HttpGet("settings")]
    public async Task<IActionResult> Settings()
    {
        var snapshot = await _accountCenter.GetSnapshotAsync(User);
        if (snapshot is null)
        {
            return Challenge();
        }

        return View(new SettingsViewModel
        {
            OrganizationName = snapshot.OrganizationName,
            OrganizationTimeZoneId = snapshot.OrganizationTimeZoneId,
            CanSwitchWorkspace = snapshot.CanSwitchWorkspace,
        });
    }

    [Authorize]
    [HttpGet("security")]
    public IActionResult Security()
    {
        return View(new ChangePasswordViewModel());
    }

    [Authorize]
    [HttpPost("security")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Security(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code == "PasswordMismatch"))
            {
                ModelState.AddModelError(nameof(model.CurrentPassword), _localizer["Account_InvalidCurrentPassword"].Value);
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            model.CurrentPassword = string.Empty;
            model.NewPassword = string.Empty;
            model.ConfirmPassword = string.Empty;
            return View(model);
        }

        await _signInManager.RefreshSignInAsync(user);
        TempData["Success"] = _localizer["Account_PasswordChanged"].Value;
        return RedirectToAction(nameof(Security));
    }

    [Authorize]
    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous]
    [HttpGet("access-denied")]
    public IActionResult AccessDenied()
    {
        return View();
    }

    private IActionResult RedirectToStaffHome(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "Operations");
    }

    private static ProfileViewModel MapProfile(AccountCenterSnapshot snapshot) =>
        new()
        {
            Email = snapshot.Email,
            DisplayName = snapshot.DisplayName,
            PhoneNumber = snapshot.PhoneNumber,
            Initials = snapshot.Initials,
            OrganizationName = snapshot.OrganizationName,
            MembershipRoleLabel = snapshot.MembershipRoleLabel,
            MembershipStatusLabel = snapshot.MembershipStatusLabel,
            OrganizationTimeZoneId = snapshot.OrganizationTimeZoneId,
        };
}
