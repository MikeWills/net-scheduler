namespace NcsScheduler.Models.Domain;

/// <summary>
/// Explicit assignment for a specific session — overrides the standing assignment.
/// Used for substitutes and volunteers.
/// </summary>
public class SessionAssignment
{
    public int Id { get; set; }
    public int NetSessionId { get; set; }
    public NetSession NetSession { get; set; } = null!;
    public int NetControllerId { get; set; }
    public NetController NetController { get; set; } = null!;
    public AssignmentType AssignmentType { get; set; }
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Scheduled;
    public string? AssignedByUserId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    // TODO: Expose Notes in the UI so band coordinators (and admins) can attach a short
    // note to any session assignment (e.g. "covering for vacation", "confirmed by email").
    // - Add [MaxLength(255)] and enforce via a migration.
    // - Add an optional Notes text input to the "Assign a Sub" form in Assignments/Index.cshtml
    //   and to the calendar's assignment modal/action if one is added later.
    // - Display the note (if present) as a small muted line beneath the callsign in both
    //   the calendar cell and the exceptions list.
    public string? Notes { get; set; }
}
