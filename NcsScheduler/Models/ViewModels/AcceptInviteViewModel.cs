using System.ComponentModel.DataAnnotations;

namespace NcsScheduler.Models.ViewModels;

public class AcceptInviteViewModel
{
    public string Token { get; set; } = "";
    public string Email { get; set; } = "";
    public string Callsign { get; set; } = "";
    public string Name { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    public string Password { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";
}
