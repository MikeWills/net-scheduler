using System.ComponentModel.DataAnnotations;

namespace NcsScheduler.Models.ViewModels;

public class InviteViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    [Display(Name = "Net Controller")]
    public int NetControllerId { get; set; }
}
