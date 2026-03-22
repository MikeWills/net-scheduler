using NcsScheduler.Models.Domain;

namespace NcsScheduler.Models.ViewModels;

public class BcCalendarViewModel
{
    /// <summary>Sunday of the displayed week.</summary>
    public DateOnly WeekStart { get; set; }

    /// <summary>Saturday of the displayed week.</summary>
    public DateOnly WeekEnd { get; set; }

    /// <summary>One row per net that is scheduled at least one day this week.</summary>
    public List<CalendarNetRow> Rows { get; set; } = [];
}

public class CalendarNetRow
{
    public Net Net { get; set; } = null!;

    /// <summary>
    /// Index 0 = Sunday … 6 = Saturday.
    /// Null means the net does not run on that day of week.
    /// </summary>
    public CalendarCell?[] Cells { get; set; } = new CalendarCell?[7];
}

public class CalendarCell
{
    public DateOnly Date { get; set; }
    public int? SessionId { get; set; }

    // Resolved assignment for this session
    public int? NetControllerId { get; set; }
    public string? Callsign { get; set; }
    public string? MemberNumber { get; set; }
    public string? CopyPasteFormat => string.IsNullOrWhiteSpace(MemberNumber)
        ? Callsign
        : $"{Callsign} #{MemberNumber}";

    public bool NeedsNcs { get; set; }
    public bool HasVolunteer { get; set; }
    public AssignmentType? AssignmentType { get; set; }

    // NCS hover-tooltip data (populated for assigned cells only)
    public DateOnly? LastScheduledDate { get; set; }
    public List<string> StandingNetNames { get; set; } = [];

    // Backup request info
    public bool BackupRequested { get; set; }
    public List<string> BackupCallsigns { get; set; } = [];

    // Change detection vs. previous week's same day
    public bool IsChanged { get; set; }
    public string? PrevCallsign { get; set; }
    public bool PrevNeedsNcs { get; set; }
}
