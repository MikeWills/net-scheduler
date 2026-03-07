using System.ComponentModel.DataAnnotations;

namespace NcsScheduler.Models.ViewModels;

public class ResetPasswordViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Token { get; set; } = "";

    [Required]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
    [Display(Name = "New Password")]
    public string NewPassword { get; set; } = "";

    [Required]
    [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = "";
}
