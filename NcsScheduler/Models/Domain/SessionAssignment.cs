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
    public string? Notes { get; set; }
}
