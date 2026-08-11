using System.ComponentModel.DataAnnotations;

namespace WorkshopOS.Web.Models.Account;

public sealed class ProfileViewModel
{
    public string Email { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    [Display(Name = "Field_PhoneNumber")]
    [Phone]
    public string? PhoneNumber { get; set; }

    public string Initials { get; set; } = string.Empty;

    public string? OrganizationName { get; set; }

    public string? MembershipRoleLabel { get; set; }

    public string? MembershipStatusLabel { get; set; }

    public string? OrganizationTimeZoneId { get; set; }
}

public sealed class ChangePasswordViewModel
{
    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Account_CurrentPassword")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Account_NewPassword")]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Account_ConfirmPassword")]
    [Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class SettingsViewModel
{
    public string? OrganizationName { get; set; }

    public string? OrganizationTimeZoneId { get; set; }

    public bool CanSwitchWorkspace { get; set; }
}
