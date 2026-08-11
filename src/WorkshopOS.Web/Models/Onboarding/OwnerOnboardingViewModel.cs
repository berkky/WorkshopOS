using System.ComponentModel.DataAnnotations;
using WorkshopOS.Web;

namespace WorkshopOS.Web.Models.Onboarding;

public sealed class OwnerOnboardingViewModel
{
    [Required]
    [MaxLength(160)]
    [Display(Name = "Field_OwnerName")]
    public string OwnerDisplayName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [Display(Name = "Field_Email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Field_Password")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Validation_PasswordsDoNotMatch")]
    [Display(Name = "Field_ConfirmPassword")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required]
    [MaxLength(160)]
    [Display(Name = "Field_BusinessName")]
    public string OrganizationName { get; set; } = string.Empty;

    [Required]
    [MaxLength(160)]
    [Display(Name = "Field_FirstLocationName")]
    public string WorkshopLocationName { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    [Display(Name = "Field_LocationCode")]
    public string WorkshopLocationCode { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [Display(Name = "Field_TimeZone")]
    public string TimeZoneId { get; set; } = string.Empty;

    [Required]
    [MaxLength(3)]
    [MinLength(3)]
    [Display(Name = "Field_Currency")]
    public string CurrencyCode { get; set; } = string.Empty;
}
