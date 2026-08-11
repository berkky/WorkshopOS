using System.ComponentModel.DataAnnotations;
using WorkshopOS.Web;

namespace WorkshopOS.Web.Models.Account;

public sealed class LoginViewModel
{
    [Required]
    [EmailAddress]
    [Display(Name = "Login_Email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Login_Password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Login_RememberMe")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}
