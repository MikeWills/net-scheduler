namespace NcsScheduler.Models.Domain;

/// <summary>
/// Many-to-many: which controllers are in the backup pool for a net.
/// Controllers can be shared across nets.
/// </summary>
public class NetControllerPool
{
    public int NetId { get; set; }
    public Net Net { get; set; } = null!;
    public int NetControllerId { get; set; }
    public NetController NetController { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}
