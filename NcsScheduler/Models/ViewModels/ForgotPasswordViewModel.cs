using System.ComponentModel.DataAnnotations;

namespace NcsScheduler.Models.ViewModels;

public class ForgotPasswordViewModel
{
    [Required]
    [EmailAddress]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = "";
}
