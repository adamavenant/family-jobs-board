using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Domain.Jobs;

namespace FamilyJobsBoard.Application.Today;

/// <summary>
/// Materializes recurring-series occurrences through a requested date. Shared by the daily
/// agenda and the calendar so both read the exact same generation guarantee and never drift.
/// </summary>
internal static class RecurringOccurrenceGenerator
{
    public static async Task EnsureGeneratedThroughAsync(
        ITodayBoardRepository repository,
        IHouseholdClock clock,
        DateOnly requestedDate,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await GenerateOnceAsync(repository, clock, requestedDate, cancellationToken);
                return;
            }
            catch (RecurringJobGenerationConflictException) when (attempt < 2)
            {
                // Another request changed or generated the same series. The repository has
                // cleared its stale tracking state, so rebuild from the committed definition.
            }
        }
    }

    private static async Task GenerateOnceAsync(
        ITodayBoardRepository repository,
        IHouseholdClock clock,
        DateOnly requestedDate,
        CancellationToken cancellationToken)
    {
        var rollingHorizon = clock.Today.AddDays(55);
        var horizon = requestedDate > rollingHorizon ? requestedDate : rollingHorizon;
        var seriesToAdvance = await repository.GetRecurringJobSeriesNeedingGenerationAsync(
            horizon,
            cancellationToken);
        if (seriesToAdvance.Count == 0)
        {
            return;
        }

        var slots = await repository.GetRecurringJobSlotsAsync(
            seriesToAdvance.Select(series => series.Id).ToArray(),
            cancellationToken);
        var occupiedBySeries = slots
            .GroupBy(slot => slot.SeriesId)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(slot => new[]
                    {
                        slot.ScheduledDate,
                        slot.OriginalScheduledDate,
                    })
                    .ToHashSet());
        var occurrences = new List<Job>();
        foreach (var series in seriesToAdvance)
        {
            var occupied = occupiedBySeries.GetValueOrDefault(series.Id) ?? [];
            foreach (var occurrence in series.GenerateOccurrencesThrough(horizon))
            {
                if (occupied.Add(occurrence.Date))
                {
                    occurrences.Add(CreateOccurrence(series, occurrence));
                }
            }
        }

        await repository.AddJobsAsync(occurrences, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }

    public static Job CreateOccurrence(
        RecurringJobSeries series,
        RecurringJobOccurrence occurrence)
    {
        return new Job(
            Guid.NewGuid(),
            occurrence.ChildId,
            series.Name,
            series.Description,
            series.Points,
            occurrence.Date,
            series.AgendaPeriod,
            series.ScheduledTime,
            series.Id,
            series.Frequency);
    }
}

public sealed class RecurringJobGenerationConflictException : Exception
{
    public RecurringJobGenerationConflictException(Exception innerException)
        : base("Recurring jobs changed while occurrences were being generated.", innerException)
    {
    }
}
