using FamilyJobsBoard.Domain.Jobs;
using Xunit;

namespace FamilyJobsBoard.Domain.Tests;

public sealed class DailyJobSeriesTests
{
    [Fact]
    public void Daily_series_generates_each_date_through_its_end_date()
    {
        var series = CreateSeries(
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
    public void Daily_series_keeps_schedule_and_trimmed_job_details()
    {
        var series = new DailyJobSeries(
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
    }

    [Fact]
    public void Daily_series_rejects_an_end_before_its_start()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSeries(
            startDate: new DateOnly(2026, 9, 2),
            endDate: new DateOnly(2026, 9, 1)));
    }

    private static DailyJobSeries CreateSeries(DateOnly startDate, DateOnly? endDate)
    {
        return new DailyJobSeries(
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
}
