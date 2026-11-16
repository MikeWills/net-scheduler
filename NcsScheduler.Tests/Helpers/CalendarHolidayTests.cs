using NcsScheduler.Helpers;
using NcsScheduler.Models.Domain;

namespace NcsScheduler.Tests.Helpers;

public class CalendarHolidayTests
{
    // Week of Sun Nov 22 – Sat Nov 28, 2026 (Thanksgiving is Thu Nov 26).
    private static readonly DateOnly WeekStart = new(2026, 11, 22);

    private static Holiday H(int year, int month, int day, string name)
        => new() { Date = new DateOnly(year, month, day), Name = name, Year = year };

    [Fact]
    public void Holiday_lands_in_its_own_day_column()
    {
        var labels = CalendarLayout.HolidayLabelsForWeek(WeekStart, [H(2026, 11, 26, "Thanksgiving Day")]);

        Assert.Equal(7, labels.Length);
        Assert.Equal("Thanksgiving Day", labels[4]); // Sun=0 … Thu=4
        Assert.All(labels.Where((_, i) => i != 4), Assert.Null);
    }

    [Fact]
    public void Holidays_outside_the_week_are_ignored()
    {
        var labels = CalendarLayout.HolidayLabelsForWeek(WeekStart,
            [H(2026, 11, 21, "Day before week"), H(2026, 11, 29, "Day after week")]);

        Assert.All(labels, Assert.Null);
    }

    [Fact]
    public void Week_without_holidays_has_no_labels()
    {
        var labels = CalendarLayout.HolidayLabelsForWeek(WeekStart, []);

        Assert.All(labels, Assert.Null);
    }

    [Fact]
    public void Two_holidays_on_one_day_are_joined()
    {
        var labels = CalendarLayout.HolidayLabelsForWeek(WeekStart,
            [H(2026, 11, 26, "Thanksgiving Day"), H(2026, 11, 26, "Club Anniversary")]);

        Assert.Equal("Thanksgiving Day / Club Anniversary", labels[4]);
    }
}
