using NcsScheduler.Helpers;

namespace NcsScheduler.Tests.Helpers;

public class DateConverterTests
{
    // ── ToEasternDate ────────────────────────────────────────────────────────
    // Real net times used throughout this app: 20:00z is a typical evening net
    // (no calendar-day rollover), 03:00z is a typical "late night UTC" net that
    // actually falls on the *previous* Eastern calendar day.

    [Theory]
    [InlineData(2026, 7, 15, 20, 0, 2026, 7, 15)]  // EDT (UTC-4), same day: 20:00z -> 4:00 PM ET
    [InlineData(2026, 7, 15, 3, 0, 2026, 7, 14)]   // EDT, rollover: 03:00z Wed -> 11:00 PM Tue ET
    [InlineData(2026, 1, 15, 20, 0, 2026, 1, 15)]  // EST (UTC-5), same day: 20:00z -> 3:00 PM ET
    [InlineData(2026, 1, 15, 3, 0, 2026, 1, 14)]   // EST, rollover: 03:00z Thu -> 10:00 PM Wed ET
    public void ToEasternDate_ConvertsUtcSessionDateToCorrectEasternCalendarDay(
        int utcYear, int utcMonth, int utcDay, int utcHour, int utcMinute,
        int expectedYear, int expectedMonth, int expectedDay)
    {
        var utcDate = new DateOnly(utcYear, utcMonth, utcDay);
        var utcTime = new TimeOnly(utcHour, utcMinute);

        var result = DateConverter.ToEasternDate(utcDate, utcTime);

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), result);
    }

    // ── ToUtcSessionDate ─────────────────────────────────────────────────────
    // Inverse of the cases above: a user picks an Eastern calendar date for a
    // net that runs late at night, and the stored UTC SessionDate should land
    // on the *next* UTC calendar day.

    [Theory]
    [InlineData(2026, 7, 15, 20, 0, 2026, 7, 15)]  // EDT, same day
    [InlineData(2026, 7, 14, 3, 0, 2026, 7, 15)]   // EDT, rolls forward to next UTC day
    [InlineData(2026, 1, 15, 20, 0, 2026, 1, 15)]  // EST, same day
    [InlineData(2026, 1, 14, 3, 0, 2026, 1, 15)]   // EST, rolls forward to next UTC day
    public void ToUtcSessionDate_ConvertsEasternDateToCorrectUtcSessionDate(
        int easternYear, int easternMonth, int easternDay, int utcHour, int utcMinute,
        int expectedYear, int expectedMonth, int expectedDay)
    {
        var easternDate = new DateOnly(easternYear, easternMonth, easternDay);
        var utcTime = new TimeOnly(utcHour, utcMinute);

        var result = DateConverter.ToUtcSessionDate(easternDate, utcTime);

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), result);
    }

    // ── Round-trip across DST transitions ───────────────────────────────────
    // ToUtcSessionDate and ToEasternDate are meant to be exact inverses. Round-
    // tripping through both across the 2026 DST boundaries (spring forward
    // Mar 8, fall back Nov 1) is a strong general check that the conversion
    // isn't using a fixed offset that goes stale the moment the clocks change --
    // exactly the class of bug already fixed once in Unavailability validation.

    [Theory]
    [InlineData(2026, 3, 6)]   // days surrounding the spring-forward transition
    [InlineData(2026, 3, 7)]
    [InlineData(2026, 3, 8)]
    [InlineData(2026, 3, 9)]
    [InlineData(2026, 10, 30)] // days surrounding the fall-back transition
    [InlineData(2026, 10, 31)]
    [InlineData(2026, 11, 1)]
    [InlineData(2026, 11, 2)]
    public void ToUtcSessionDate_AndBack_RoundTripsAcrossDstTransitions(int year, int month, int day)
    {
        var easternDate = new DateOnly(year, month, day);

        foreach (var utcTime in new[] { new TimeOnly(3, 0), new TimeOnly(20, 0) })
        {
            var utcSessionDate = DateConverter.ToUtcSessionDate(easternDate, utcTime);
            var roundTripped = DateConverter.ToEasternDate(utcSessionDate, utcTime);

            Assert.Equal(easternDate, roundTripped);
        }
    }

    // ── TodayEastern ─────────────────────────────────────────────────────────
    // No clock abstraction to inject a fixed "now", so this is a light sanity
    // check rather than a precise assertion: it should return a real date
    // within a day of UTC "now" (the two can only ever differ by the Eastern
    // offset, never more than a calendar day) and never throw.

    [Fact]
    public void TodayEastern_ReturnsADateCloseToUtcNow()
    {
        var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = DateConverter.TodayEastern();

        var dayDiff = Math.Abs(result.DayNumber - utcToday.DayNumber);
        Assert.True(dayDiff <= 1, $"Expected {result} to be within 1 day of UTC today ({utcToday}).");
    }
}
