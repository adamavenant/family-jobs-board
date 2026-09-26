using FamilyJobsBoard.Domain.Jobs;
using Xunit;

namespace FamilyJobsBoard.Domain.Tests;

public sealed class RotatingJobSeriesTests
{
    private static readonly Guid Ava = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Ben = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Cleo = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Adult = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void Two_children_alternate_daily_occurrences()
    {
        var series = CreateDaily([Ava, Ben], new DateOnly(2026, 9, 1));

        var occurrences = series.GenerateOccurrencesThrough(new DateOnly(2026, 9, 4));

        Assert.Equal(
            [
                new RecurringJobOccurrence(new DateOnly(2026, 9, 1), Ava),
                new RecurringJobOccurrence(new DateOnly(2026, 9, 2), Ben),
                new RecurringJobOccurrence(new DateOnly(2026, 9, 3), Ava),
                new RecurringJobOccurrence(new DateOnly(2026, 9, 4), Ben),
            ],
            occurrences);
        Assert.True(series.TakesTurns);
        Assert.Equal(Ava, series.ChildId);
    }

    [Fact]
    public void Three_children_rotate_round_robin_across_weekly_occurrences()
    {
        var series = RecurringJobSeries.Weekly(
            Guid.NewGuid(),
            Cleo,
            Adult,
            "Tidy the table",
            string.Empty,
            1,
            AgendaPeriod.Evening,
            null,
            new DateOnly(2026, 9, 7),
            null,
            [DayOfWeek.Monday, DayOfWeek.Wednesday],
            rotationChildIds: [Cleo, Ava, Ben]);

        var occurrences = series.GenerateOccurrencesThrough(new DateOnly(2026, 9, 21));

        Assert.Equal(
            [Cleo, Ava, Ben, Cleo, Ava],
            occurrences.Select(occurrence => occurrence.ChildId));
        Assert.Equal(
            [
                new DateOnly(2026, 9, 7),
                new DateOnly(2026, 9, 9),
                new DateOnly(2026, 9, 14),
                new DateOnly(2026, 9, 16),
                new DateOnly(2026, 9, 21),
            ],
            occurrences.Select(occurrence => occurrence.Date));
    }

    [Fact]
    public void Incremental_generation_continues_the_rotation()
    {
        var series = CreateDaily([Ava, Ben, Cleo], new DateOnly(2026, 9, 1));

        var first = series.GenerateOccurrencesThrough(new DateOnly(2026, 9, 2));
        var second = series.GenerateOccurrencesThrough(new DateOnly(2026, 9, 5));

        Assert.Equal([Ava, Ben], first.Select(occurrence => occurrence.ChildId));
        Assert.Equal([Cleo, Ava, Ben], second.Select(occurrence => occurrence.ChildId));
        Assert.Equal(2, series.NextTurnIndex);
    }

    [Fact]
    public void Series_without_a_rotation_assigns_every_occurrence_to_its_child()
    {
        var series = RecurringJobSeries.Daily(
            Guid.NewGuid(),
            Ava,
            Adult,
            "Feed the cat",
            string.Empty,
            1,
            AgendaPeriod.Morning,
            null,
            new DateOnly(2026, 9, 1),
            null);

        var occurrences = series.GenerateOccurrencesThrough(new DateOnly(2026, 9, 3));

        Assert.False(series.TakesTurns);
        Assert.All(occurrences, occurrence => Assert.Equal(Ava, occurrence.ChildId));
        Assert.Equal(0, series.NextTurnIndex);
    }

    [Fact]
    public void Rotation_needs_at_least_two_children()
    {
        Assert.Throws<ArgumentException>(() => CreateDaily([Ava], new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public void Rotation_rejects_duplicate_or_empty_children()
    {
        Assert.Throws<ArgumentException>(() => CreateDaily([Ava, Ava], new DateOnly(2026, 9, 1)));
        Assert.Throws<ArgumentException>(() => CreateDaily([Ava, Guid.Empty], new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public void Rotation_must_start_with_the_assigned_child()
    {
        Assert.Throws<ArgumentException>(() => RecurringJobSeries.Daily(
            Guid.NewGuid(),
            Ben,
            Adult,
            "Tidy the table",
            string.Empty,
            1,
            AgendaPeriod.Evening,
            null,
            new DateOnly(2026, 9, 1),
            null,
            rotationChildIds: [Ava, Ben]));
    }

    [Fact]
    public void Matching_distinguishes_rotation_order()
    {
        var series = CreateDaily([Ava, Ben], new DateOnly(2026, 9, 1));

        Assert.True(Matches(series, [Ava, Ben]));
        Assert.False(Matches(series, [Ben, Ava]));
        Assert.False(Matches(series, null));
    }

    private static bool Matches(RecurringJobSeries series, IReadOnlyList<Guid>? rotation)
    {
        return series.MatchesDaily(
            Ava,
            Adult,
            "Tidy the table",
            string.Empty,
            1,
            AgendaPeriod.Evening,
            null,
            new DateOnly(2026, 9, 1),
            null,
            rotation);
    }

    private static RecurringJobSeries CreateDaily(IReadOnlyList<Guid> rotation, DateOnly startDate)
    {
        return RecurringJobSeries.Daily(
            Guid.NewGuid(),
            rotation[0],
            Adult,
            "Tidy the table",
            string.Empty,
            1,
            AgendaPeriod.Evening,
            null,
            startDate,
            null,
            rotationChildIds: rotation);
    }
}
