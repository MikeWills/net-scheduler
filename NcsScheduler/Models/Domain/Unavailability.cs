namespace NcsScheduler.Models.Domain;

/// <summary>
/// Records that a controller is unavailable over a date range.
/// EndDate == StartDate means a single day.
/// NetId = null means unavailable for all nets in the range.
/// </summary>
public class Unavailability
{
    public int Id { get; set; }
    public int NetControllerId { get; set; }
    public NetController NetController { get; set; } = null!;
    public int? NetId { get; set; }
    public Net? Net { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Reason { get; set; }
    public string MarkedByUserId { get; set; } = "";
    public DateTime MarkedAt { get; set; } = DateTime.UtcNow;
}
