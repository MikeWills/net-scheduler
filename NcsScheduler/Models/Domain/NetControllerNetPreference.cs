namespace NcsScheduler.Models.Domain;

/// <summary>
/// Records which nets a net controller is willing to run.
/// If a controller has no preferences, they see all nets (backward-compatible default).
/// </summary>
public class NetControllerNetPreference
{
    public int Id { get; set; }
    public int NetControllerId { get; set; }
    public NetController NetController { get; set; } = null!;
    public int NetId { get; set; }
    public Net Net { get; set; } = null!;
}
