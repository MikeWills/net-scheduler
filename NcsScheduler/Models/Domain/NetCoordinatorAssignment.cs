namespace NcsScheduler.Models.Domain;

/// <summary>
/// Tracks which band coordinator manages which net over time.
/// EndDate = null means currently active.
/// </summary>
public class NetCoordinatorAssignment
{
    public int Id { get; set; }
    public int NetId { get; set; }
    public Net Net { get; set; } = null!;
    public int BandCoordinatorId { get; set; }
    public BandCoordinator BandCoordinator { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}
