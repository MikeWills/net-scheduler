namespace NcsScheduler.Models.Domain;

public class BandCoordinator
{
    public int Id { get; set; }
    public int NetControllerId { get; set; }
    public NetController NetController { get; set; } = null!;
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<NetCoordinatorAssignment> NetAssignments { get; set; } = [];
}
