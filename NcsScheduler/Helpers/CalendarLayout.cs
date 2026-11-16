using NcsScheduler.Models.Domain;

namespace NcsScheduler.Helpers;

/// <summary>
/// Decides which cells and rows the BC weekly calendar renders.
///
/// Pulled out of AssignmentsController.Calendar so the day-of-week vs. ad-hoc
/// session logic can be unit tested without an EF/Identity harness.
/// </summary>
public static class CalendarLayout
{
    /// <summary>
    /// True if a day column should get a cell for this net.
    /// </summary>
    /// <param name="utcActiveDays">UTC days-of-week the net runs, from its active schedule rules.</param>
    /// <param name="utcDay">The UTC day-of-week this column resolves to.</param>
    /// <param name="sessionExists">True if a NetSession already exists for this net and date.</param>
    public static bool ShouldRenderCell(
        IReadOnlySet<DayOfWeek> utcActiveDays, DayOfWeek utcDay, bool sessionExists)
        => sessionExists || utcActiveDays.Contains(utcDay);

    /// <summary>
    /// True if the net should get a row in the grid at all.
    /// </summary>
    /// <param name="hasActiveRules">True if the net has at least one active schedule rule.</param>
    /// <param name="hasAnyCell">True if any day column produced a cell.</param>
    public static bool ShouldRenderRow(bool hasActiveRules, bool hasAnyCell)
        => hasActiveRules || hasAnyCell;

    /// <summary>
    /// Holiday name for each of the week's seven Eastern day columns (Sun..Sat), or null.
    /// Holiday.Date is the holiday's own calendar date, compared directly against the
    /// Eastern column dates. Two holidays on one day are joined with " / ".
    /// </summary>
    public static string?[] HolidayLabelsForWeek(DateOnly weekStart, IEnumerable<Holiday> holidays)
    {
        var labels = new string?[7];
        foreach (var h in holidays.OrderBy(h => h.Date))
        {
            int i = h.Date.DayNumber - weekStart.DayNumber;
            if (i < 0 || i > 6) continue;
            labels[i] = labels[i] is null ? h.Name : $"{labels[i]} / {h.Name}";
        }
        return labels;
    }
}
