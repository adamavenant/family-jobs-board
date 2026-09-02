using FamilyJobsBoard.Domain.Jobs;
using Xunit;

namespace FamilyJobsBoard.Domain.Tests;

public sealed class RecurringJobSeriesTests
{
    [Fact]
    public void Daily_series_generates_each_date_through_its_end_date()
    {
        var series = CreateDailySeries(
            startDate: new DateOnly(2026, 9, 1),
            endDate: new DateOnly(2026, 9, 3));

        var dates = series.GenerateThrough(new DateOnly(2026, 10, 26));

        Assert.Equal(
            [
                new DateOnly(2026, 9, 1),
                new DateOnly(2026, 9, 2),
                new DateOnly(2026, 9, 3),
            ],
            dates);
        Assert.Equal(new DateOnly(2026, 9, 3), series.LastOccurrenceDate(new DateOnly(2026, 10, 26)));
        Assert.Equal(new DateOnly(2026, 9, 3), series.GeneratedThrough);
        Assert.Empty(series.GenerateThrough(new DateOnly(2026, 10, 26)));
    }

    [Fact]
    public void Weekly_series_generates_only_selected_weekdays_across_a_year_boundary()
    {
        var series = CreateWeeklySeries(
            startDate: new DateOnly(2026, 12, 28),
            endDate: new DateOnly(2027, 1, 7),
            weekdays: [DayOfWeek.Monday, DayOfWeek.Thursday]);

        var dates = series.GenerateThrough(new DateOnly(2027, 2, 1));

        Assert.Equal(
            [
                new DateOnly(2026, 12, 28),
                new DateOnly(2026, 12, 31),
                new DateOnly(2027, 1, 4),
                new DateOnly(2027, 1, 7),
            ],
            dates);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Thursday], series.SelectedWeekdays());
        Assert.Empty(series.GenerateThrough(new DateOnly(2027, 2, 1)));
    }

    [Fact]
    public void Weekly_series_handles_leap_day_and_a_daylight_saving_boundary_as_local_dates()
    {
        var leapDay = CreateWeeklySeries(
            startDate: new DateOnly(2028, 2, 27),
            endDate: new DateOnly(2028, 3, 1),
            weekdays: [DayOfWeek.Tuesday]);
        var daylightSaving = CreateWeeklySeries(
            startDate: new DateOnly(2026, 10, 24),
            endDate: new DateOnly(2026, 11, 2),
            weekdays: [DayOfWeek.Sunday]);

        Assert.Equal([new DateOnly(2028, 2, 29)], leapDay.GenerateThrough(new DateOnly(2028, 3, 1)));
        Assert.Equal(
            [new DateOnly(2026, 10, 25), new DateOnly(2026, 11, 1)],
            daylightSaving.GenerateThrough(new DateOnly(2026, 11, 2)));
    }

    [Fact]
    public void Weekly_series_respects_start_and_end_dates_that_are_not_selected_weekdays()
    {
        var series = CreateWeeklySeries(
            startDate: new DateOnly(2026, 9, 2),
            endDate: new DateOnly(2026, 9, 8),
            weekdays: [DayOfWeek.Monday, DayOfWeek.Wednesday]);

        Assert.Equal(
            [new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 7)],
            series.GenerateThrough(new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void Weekly_series_rejects_empty_or_duplicate_weekdays()
    {
        Assert.Throws<ArgumentException>(() => CreateWeeklySeries(
            new DateOnly(2026, 9, 1),
            null,
            []));
        Assert.Throws<ArgumentException>(() => CreateWeeklySeries(
            new DateOnly(2026, 9, 1),
            null,
            [DayOfWeek.Monday, DayOfWeek.Monday]));
    }

    [Fact]
    public void Recurring_series_keeps_schedule_and_trimmed_job_details()
    {
        var series = RecurringJobSeries.Daily(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "  Feed the dog  ",
            "  Fill both bowls.  ",
            3,
            AgendaPeriod.Morning,
            new TimeOnly(7, 30),
            new DateOnly(2026, 9, 1),
            null);

        Assert.Equal("Feed the dog", series.Name);
        Assert.Equal("Fill both bowls.", series.Description);
        Assert.Equal(AgendaPeriod.Morning, series.AgendaPeriod);
        Assert.Equal(new TimeOnly(7, 30), series.ScheduledTime);
        Assert.Equal(RecurrenceFrequency.Daily, series.Frequency);
    }

    [Fact]
    public void Recurring_series_rejects_an_end_before_its_start()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateDailySeries(
            startDate: new DateOnly(2026, 9, 2),
            endDate: new DateOnly(2026, 9, 1)));
    }

    private static RecurringJobSeries CreateDailySeries(DateOnly startDate, DateOnly? endDate)
    {
        return RecurringJobSeries.Daily(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Feed the dog",
            "Fill both bowls.",
            3,
            AgendaPeriod.Morning,
            new TimeOnly(7, 30),
            startDate,
            endDate);
    }

    private static RecurringJobSeries CreateWeeklySeries(
        DateOnly startDate,
        DateOnly? endDate,
        IReadOnlyCollection<DayOfWeek> weekdays)
    {
        return RecurringJobSeries.Weekly(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Feed the dog",
            "Fill both bowls.",
            3,
            AgendaPeriod.Morning,
            new TimeOnly(7, 30),
            startDate,
            endDate,
            weekdays);
    }
}
