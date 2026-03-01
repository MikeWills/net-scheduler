namespace NcsScheduler.Models.Domain;

/// <summary>
/// Defines which days of the week a net runs.
/// Holiday nets have no rules — their sessions are created ad-hoc.
/// </summary>
public class NetScheduleRule
{
    public int Id { get; set; }
    public int NetId { get; set; }
    public Net Net { get; set; } = null!;
    public DayOfWeek DayOfWeek { get; set; }
    public bool IsActive { get; set; } = true;
}
