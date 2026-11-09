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
}
