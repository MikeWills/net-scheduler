using System.ComponentModel.DataAnnotations;

namespace NcsScheduler.Models.ViewModels;

public class HolidayEditViewModel
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "Date")]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Required]
    [StringLength(100)]
    [Display(Name = "Holiday Name")]
    public string Name { get; set; } = "";
}
