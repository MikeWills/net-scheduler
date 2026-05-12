namespace NcsScheduler.Helpers;

/// <summary>
/// Converts between Eastern local dates (what users see) and UTC session dates
/// (what the database stores). For overnight nets (e.g. 03:00z = 10 PM Eastern),
/// the UTC date is one day ahead of the Eastern date.
/// </summary>
public static class DateConverter
{
    private static readonly TimeZoneInfo EasternZone = LoadEasternZone();

    private static TimeZoneInfo LoadEasternZone()
    {
        // Windows uses "Eastern Standard Time"; Linux/macOS use "America/New_York"
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Eastern Standard Time", out var tz)) return tz;
        if (TimeZoneInfo.TryFindSystemTimeZoneById("America/New_York", out tz)) return tz;
        return TimeZoneInfo.Utc;
    }

    /// <summary>
    /// Converts a user-entered Eastern local date to the UTC SessionDate stored in the database.
    /// Example: Eastern Monday + 03:00z net → UTC Tuesday (because 10 PM Mon ET = 03:00 Tue UTC).
    /// </summary>
    public static DateOnly ToUtcSessionDate(DateOnly easternDate, TimeOnly utcTime)
    {
        // Figure out what time the net runs in Eastern
        var sampleUtc = easternDate.ToDateTime(utcTime, DateTimeKind.Utc);
        var easternNetTime = TimeOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(sampleUtc, EasternZone));

        // Combine the user's Eastern date with the Eastern net time, then convert to UTC
        var localDt = easternDate.ToDateTime(easternNetTime);
        var utcDt = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localDt, DateTimeKind.Unspecified), EasternZone);
        return DateOnly.FromDateTime(utcDt);
    }

    /// <summary>
    /// Converts a UTC SessionDate back to the Eastern local date for display.
    /// Example: UTC Tuesday + 03:00z net → Eastern Monday (because 03:00 Tue UTC = 10 PM Mon ET).
    /// </summary>
    public static DateOnly ToEasternDate(DateOnly utcDate, TimeOnly utcTime)
    {
        var utcDt = utcDate.ToDateTime(utcTime, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcDt, EasternZone));
    }
}
