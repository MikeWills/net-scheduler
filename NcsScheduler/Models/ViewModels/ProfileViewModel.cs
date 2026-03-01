using System.ComponentModel.DataAnnotations;

namespace NcsScheduler.Models.ViewModels;

public class ProfileViewModel
{
    public string Callsign { get; set; } = "";
    public string? MemberNumber { get; set; }

    [Required]
    public string Name { get; set; } = "";

    [EmailAddress]
    public string? Email { get; set; }

    [Phone]
    public string? Phone { get; set; }

    [Display(Name = "Notify me when a slot I cover is opened")]
    public bool NotifyOnSlotOpened { get; set; }

    [Display(Name = "Notify me when I'm assigned to cover a slot")]
    public bool NotifyOnAssigned { get; set; }

    [DataType(DataType.Password)]
    [Display(Name = "New password (leave blank to keep current)")]
    public string? NewPassword { get; set; }

    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    [Compare(nameof(NewPassword))]
    public string? ConfirmPassword { get; set; }
}
