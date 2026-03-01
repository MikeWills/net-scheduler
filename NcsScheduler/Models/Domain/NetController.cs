namespace NcsScheduler.Models.Domain;

public class NetController
{
    public int Id { get; set; }

    /// <summary>FK to AspNetUsers — null until the invite is accepted.</summary>
    public string? UserId { get; set; }

    public string Callsign { get; set; } = "";
    public string? MemberNumber { get; set; }
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;

    // Email notification opt-ins
    public bool NotifyOnSlotOpened { get; set; }
    public bool NotifyOnAssigned { get; set; }

    // Navigation
    public ICollection<StandingAssignment> StandingAssignments { get; set; } = [];
    public ICollection<Unavailability> Unavailabilities { get; set; } = [];
    public ICollection<NetControllerPool> PoolMemberships { get; set; } = [];
    public ICollection<SessionAssignment> SessionAssignments { get; set; } = [];
    public BandCoordinator? BandCoordinator { get; set; }

    /// <summary>Formatted for copy-pasting to the club website (e.g. "WX0MIK #12823").</summary>
    public string CopyPasteFormat =>
        string.IsNullOrWhiteSpace(MemberNumber)
            ? Callsign
            : $"{Callsign} #{MemberNumber}";
}
