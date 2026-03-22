namespace NcsScheduler.Models.Domain;

/// <summary>
/// A materialized individual net occurrence.
/// Regular sessions are auto-generated from NetScheduleRules (rolling 8-week window).
/// Holiday sessions are created by the admin or auto-generated from the federal holiday calendar.
/// </summary>
public class NetSession
{
    public int Id { get; set; }
    public int NetId { get; set; }
    public Net Net { get; set; } = null!;
    public DateOnly SessionDate { get; set; }
    public TimeOnly ScheduledTimeUtc { get; set; }

    /// <summary>
    /// When true, a coordinator has manually flagged this session as needing a sub,
    /// regardless of whether the standing controller filed an unavailability report.
    /// </summary>
    public bool IsForcedOpen { get; set; }

    /// <summary>
    /// When true, the assigned NCS has flagged they may need a backup.
    /// Other NCS members can stand by with AssignmentType.Backup.
    /// </summary>
    public bool BackupRequested { get; set; }

    // Navigation
    public ICollection<SessionAssignment> Assignments { get; set; } = [];
}
