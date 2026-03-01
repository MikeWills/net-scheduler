namespace NcsScheduler.Models.Domain;

public class Net
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Band { get; set; }
    public string? FrequencyMhz { get; set; }
    public string? FrequencyRange { get; set; }
    public string? Description { get; set; }
    public TimeOnly ScheduledTimeUtc { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Optional season window — only month and day are compared; the stored year is ignored.
    /// Both must be set together, or leave both null for a year-round net.
    /// Handles wrap-around (e.g. Oct–Mar).
    /// </summary>
    public DateOnly? SeasonStart { get; set; }
    public DateOnly? SeasonEnd   { get; set; }

    /// <summary>
    /// Returns true if <paramref name="date"/> falls within this net's season,
    /// or if no season is configured.
    /// </summary>
    public bool IsInSeasonForDate(DateOnly date)
    {
        if (SeasonStart is null || SeasonEnd is null) return true;
        int startMD = SeasonStart.Value.Month * 100 + SeasonStart.Value.Day;
        int endMD   = SeasonEnd.Value.Month   * 100 + SeasonEnd.Value.Day;
        int dateMD  = date.Month              * 100 + date.Day;
        // Normal range (e.g. May 1 – Sep 30): start ≤ end
        if (startMD <= endMD)
            return dateMD >= startMD && dateMD <= endMD;
        // Wrap-around (e.g. Oct 1 – Mar 31): start > end
        return dateMD >= startMD || dateMD <= endMD;
    }

    // Navigation
    public ICollection<NetScheduleRule> ScheduleRules { get; set; } = [];
    public ICollection<NetSession> Sessions { get; set; } = [];
    public ICollection<StandingAssignment> StandingAssignments { get; set; } = [];
    public ICollection<NetCoordinatorAssignment> CoordinatorAssignments { get; set; } = [];
    public ICollection<NetControllerPool> ControllerPool { get; set; } = [];
}
