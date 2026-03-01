namespace NcsScheduler.Models.Domain;

/// <summary>
/// Default assignment: who normally runs which net on which day of the week.
/// EffectiveTo = null means currently active.
/// </summary>
public class StandingAssignment
{
    public int Id { get; set; }
    public int NetId { get; set; }
    public Net Net { get; set; } = null!;
    public int NetControllerId { get; set; }
    public NetController NetController { get; set; } = null!;
    public DayOfWeek DayOfWeek { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}
