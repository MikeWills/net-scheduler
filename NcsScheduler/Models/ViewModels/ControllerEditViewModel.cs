using System.ComponentModel.DataAnnotations;

namespace NcsScheduler.Models.ViewModels;

public class ControllerEditViewModel
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "Callsign")]
    public string Callsign { get; set; } = "";

    [Display(Name = "Member Number")]
    public string? MemberNumber { get; set; }

    [Required]
    public string Name { get; set; } = "";

    [EmailAddress]
    public string? Email { get; set; }

    [Phone]
    public string? Phone { get; set; }

    public bool IsActive { get; set; } = true;
}
