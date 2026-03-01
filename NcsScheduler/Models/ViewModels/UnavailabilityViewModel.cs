using System.ComponentModel.DataAnnotations;

namespace NcsScheduler.Models.ViewModels;

/// <summary>Used when editing an existing unavailability — no "must be in the future" restriction.</summary>
public class UnavailabilityEditViewModel : IValidatableObject
{
    [Required]
    [Display(Name = "Start Date")]
    public DateOnly StartDate { get; set; }

    [Required]
    [Display(Name = "End Date")]
    public DateOnly EndDate { get; set; }

    [Display(Name = "Net (leave blank for all nets)")]
    public int? NetId { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndDate < StartDate)
            yield return new ValidationResult("End Date must be on or after Start Date.", [nameof(EndDate)]);
    }
}

public class UnavailabilityCreateViewModel : IValidatableObject
{
    [Required]
    [Display(Name = "Start Date")]
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

    [Required]
    [Display(Name = "End Date")]
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

    [Display(Name = "Net (leave blank for all nets)")]
    public int? NetId { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndDate < StartDate)
            yield return new ValidationResult("End Date must be on or after Start Date.", [nameof(EndDate)]);

        if (StartDate < DateOnly.FromDateTime(DateTime.UtcNow))
            yield return new ValidationResult("Start Date cannot be in the past.", [nameof(StartDate)]);
    }
}
