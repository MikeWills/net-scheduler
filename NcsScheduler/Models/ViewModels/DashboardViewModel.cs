using NcsScheduler.Models.Domain;

namespace NcsScheduler.Models.ViewModels;

public class DashboardViewModel
{
    public string? Callsign { get; set; }
    public string? MemberNumber { get; set; }
    public string CopyPaste => string.IsNullOrWhiteSpace(MemberNumber) ? Callsign ?? "" : $"{Callsign} #{MemberNumber}";

    public List<SessionAssignment> MyExplicitAssignments { get; set; } = [];
    public List<StandingAssignment> MyStandingAssignments { get; set; } = [];
    public HashSet<DateOnly> MyUnavailableDates { get; set; } = [];
    public List<OpenSlotItem> OpenSlots { get; set; } = [];
    public List<UpcomingNetItem> MyUpcomingNets { get; set; } = [];

    /// <summary>Absolute URL for this controller's iCal calendar feed, or null if unavailable.</summary>
    public string? IcalFeedUrl { get; set; }
}

public class UpcomingNetItem
{
    public DateOnly SessionDate { get; set; }
    public TimeOnly ScheduledTimeUtc { get; set; }
    public string NetName { get; set; } = "";
    public string? FrequencyMhz { get; set; }
    public string? FrequencyRange { get; set; }
    public bool IsSubstitute { get; set; }
}

public class OpenSlotItem
{
    public int SessionId { get; set; }
    public string NetName { get; set; } = "";
    public DateOnly SessionDate { get; set; }
    public TimeOnly ScheduledTimeUtc { get; set; }
    public bool AlreadyVolunteered { get; set; }
}
