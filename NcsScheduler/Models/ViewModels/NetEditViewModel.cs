using System.ComponentModel.DataAnnotations;

namespace NcsScheduler.Models.ViewModels;

public class NetEditViewModel
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = "";

    public string? Band { get; set; }

    [Display(Name = "Frequency")]
    [StringLength(20)]
    public string? FrequencyMhz { get; set; }

    [Display(Name = "Frequency Range")]
    [StringLength(50)]
    public string? FrequencyRange { get; set; }

    public string? Description { get; set; }

    [Required]
    [Display(Name = "Scheduled Time (UTC)")]
    public TimeOnly ScheduledTimeUtc { get; set; }

    public bool IsActive { get; set; } = true;

    [Display(Name = "Season Start")]
    public DateOnly? SeasonStart { get; set; }

    [Display(Name = "Season End")]
    public DateOnly? SeasonEnd { get; set; }

    [Display(Name = "Days this net runs")]
    public List<DayOfWeek> SelectedDays { get; set; } = [];
}
