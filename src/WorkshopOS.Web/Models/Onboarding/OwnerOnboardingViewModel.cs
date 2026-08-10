using System.ComponentModel.DataAnnotations;

namespace WorkshopOS.Web.Models.Onboarding;

public sealed class OwnerOnboardingViewModel
{
    [Required]
    [MaxLength(160)]
    [Display(Name = "Owner name")]
    public string OwnerDisplayName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required]
    [MaxLength(160)]
    [Display(Name = "Business name")]
    public string OrganizationName { get; set; } = string.Empty;

    [Required]
    [MaxLength(160)]
    [Display(Name = "First location name")]
    public string WorkshopLocationName { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    [Display(Name = "Location code")]
    public string WorkshopLocationCode { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [Display(Name = "Time zone")]
    public string TimeZoneId { get; set; } = string.Empty;

    [Required]
    [MaxLength(3)]
    [MinLength(3)]
    [Display(Name = "Currency")]
    public string CurrencyCode { get; set; } = string.Empty;
}
