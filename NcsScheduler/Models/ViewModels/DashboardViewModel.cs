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
    public List<BackupRequestItem> BackupSessions { get; set; } = [];

    /// <summary>Absolute URL for this controller's iCal calendar feed, or null if unavailable.</summary>
    public string? IcalFeedUrl { get; set; }

    /// <summary>Net IDs the controller has opted into. Empty means all nets (default).</summary>
    public HashSet<int> NetPreferenceIds { get; set; } = [];

    /// <summary>All active nets, used to render the preferences UI.</summary>
    public List<Net> AllNets { get; set; } = [];
}

public class UpcomingNetItem
{
    public int? SessionId { get; set; }
    public DateOnly SessionDate { get; set; }
    public TimeOnly ScheduledTimeUtc { get; set; }
    public string NetName { get; set; } = "";
    public string? FrequencyMhz { get; set; }
    public string? FrequencyRange { get; set; }
    public bool IsSubstitute { get; set; }
    public bool BackupRequested { get; set; }
    public int BackupCount { get; set; }
    public bool AlreadyStandingBy { get; set; }
}

public class OpenSlotItem
{
    public int SessionId { get; set; }
    public string NetName { get; set; } = "";
    public DateOnly SessionDate { get; set; }
    public TimeOnly ScheduledTimeUtc { get; set; }
    public bool AlreadyVolunteered { get; set; }
}

public class BackupRequestItem
{
    public int SessionId { get; set; }
    public string NetName { get; set; } = "";
    public DateOnly SessionDate { get; set; }
    public TimeOnly ScheduledTimeUtc { get; set; }
    public string RequestingNcsCallsign { get; set; } = "";
    public int BackupCount { get; set; }
    public bool AlreadyStandingBy { get; set; }
}
