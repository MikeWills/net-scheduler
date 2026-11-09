using NcsScheduler.Helpers;

namespace NcsScheduler.Tests.Helpers;

public class CalendarLayoutTests
{
    private static IReadOnlySet<DayOfWeek> Days(params DayOfWeek[] d) => d.ToHashSet();

    // ── Rule-driven nets: existing behavior, must not regress ────────────────

    [Fact]
    public void Cell_rendered_on_a_day_the_net_runs()
    {
        Assert.True(CalendarLayout.ShouldRenderCell(
            Days(DayOfWeek.Monday, DayOfWeek.Thursday), DayOfWeek.Monday, sessionExists: false));
    }

    [Fact]
    public void Cell_not_rendered_on_a_day_the_net_does_not_run_and_has_no_session()
    {
        Assert.False(CalendarLayout.ShouldRenderCell(
            Days(DayOfWeek.Monday), DayOfWeek.Tuesday, sessionExists: false));
    }

    [Fact]
    public void Row_rendered_for_a_net_with_rules()
    {
        Assert.True(CalendarLayout.ShouldRenderRow(hasActiveRules: true, hasAnyCell: true));
    }

    // ── Holiday / ad-hoc nets: the bug ──────────────────────────────────────

    [Fact]
    public void Cell_rendered_for_a_holiday_net_on_a_date_that_has_a_session()
    {
        // "Late 80 holiday" has no schedule rules. A session exists for this
        // date because a BC assigned someone to it, so it must appear.
        Assert.True(CalendarLayout.ShouldRenderCell(
            Days(), DayOfWeek.Tuesday, sessionExists: true));
    }

    [Fact]
    public void Cell_not_rendered_for_a_holiday_net_on_a_date_with_no_session()
    {
        Assert.False(CalendarLayout.ShouldRenderCell(
            Days(), DayOfWeek.Tuesday, sessionExists: false));
    }

    [Fact]
    public void Row_rendered_for_a_rules_less_net_that_has_a_session_this_week()
    {
        Assert.True(CalendarLayout.ShouldRenderRow(hasActiveRules: false, hasAnyCell: true));
    }

    [Fact]
    public void Row_not_rendered_for_a_rules_less_net_with_no_sessions_this_week()
    {
        // Otherwise every holiday net shows an empty row every week.
        Assert.False(CalendarLayout.ShouldRenderRow(hasActiveRules: false, hasAnyCell: false));
    }

    // ── Same class of bug for a normal net ──────────────────────────────────

    [Fact]
    public void Cell_rendered_for_a_rule_net_on_an_off_rule_day_that_has_a_session()
    {
        // A one-off session deliberately created outside the net's normal days
        // should also show up rather than being silently hidden.
        Assert.True(CalendarLayout.ShouldRenderCell(
            Days(DayOfWeek.Monday), DayOfWeek.Saturday, sessionExists: true));
    }
}
