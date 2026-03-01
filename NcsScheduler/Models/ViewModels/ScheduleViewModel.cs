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
