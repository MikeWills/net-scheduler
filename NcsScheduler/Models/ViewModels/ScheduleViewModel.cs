using NcsScheduler.Models.Domain;

namespace NcsScheduler.Models.ViewModels;

public class ScheduleViewModel
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public List<WeekRow> Weeks { get; set; } = [];
    public List<NetColumn> Nets { get; set; } = [];
}

public class WeekRow
{
    public DateOnly WeekStart { get; set; }
    public DateOnly WeekEnd { get; set; }

    /// <summary>Key: NetSession.Id → resolved slot for that session.</summary>
    public Dictionary<int, ScheduleSlot> Slots { get; set; } = [];
}

public class NetColumn
{
    public int NetId { get; set; }
    public string NetName { get; set; } = "";
    public string? Band { get; set; }
    public TimeOnly ScheduledTimeUtc { get; set; }
    public string? FrequencyMhz { get; set; }
    public string? FrequencyRange { get; set; }

    /// <summary>Ordered list of session IDs that belong to this net within each week row.</summary>
    public List<DaySession> DaySessions { get; set; } = [];
}

public class DaySession
{
    public int SessionId { get; set; }
    public DateOnly SessionDate { get; set; }
    public string DayLabel { get; set; } = "";   // e.g. "Sun", "Mon"
}

public class ScheduleSlot
{
    public int SessionId { get; set; }
    public int NetId { get; set; }
    public DateOnly SessionDate { get; set; }

    /// <summary>Eastern local time of the session — for display reference.</summary>
    public TimeOnly EasternLocalTime { get; set; }

    /// <summary>UTC time of the session — used to compute the broadcast sort key.</summary>
    public TimeOnly ScheduledTimeUtc { get; set; }

    /// <summary>
    /// Display sort key using a "broadcast clock": overnight UTC sessions (before 10:00z)
    /// are shifted by +24 h so they sort after afternoon/evening sessions.
    /// Result for these nets: 10m (18:00z=18) → Early (03:00z=27) → 160m (04:00z=28) → Late (05:00z=29).
    /// The 10:00z threshold is well clear of all overnight US nets (≤05:00z) and
    /// all daytime nets (≥15:00z), and is DST-independent.
    /// </summary>
    public double BroadcastSortKey
    {
        get
        {
            double mins = ScheduledTimeUtc.ToTimeSpan().TotalMinutes;
            return mins < 10 * 60 ? mins + 1440 : mins;   // 1440 = 24 h in minutes
        }
    }

    // Resolved assignment
    public string? Callsign { get; set; }
    public string? MemberNumber { get; set; }
    public string? CopyPasteFormat => string.IsNullOrWhiteSpace(MemberNumber)
        ? Callsign
        : $"{Callsign} #{MemberNumber}";

    public bool NeedsNcs { get; set; }
    public bool HasVolunteer { get; set; }
    public AssignmentType? AssignmentType { get; set; }

    public string CssClass => (NeedsNcs, HasVolunteer) switch
    {
        (true, true) => "slot-volunteer",
        (true, false) => "slot-open",
        _ => "slot-normal"
    };
}
