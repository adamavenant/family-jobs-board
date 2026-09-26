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
        var rollingHorizon = clock.Today.AddDays(55);
        var horizon = requestedDate > rollingHorizon ? requestedDate : rollingHorizon;
        var seriesToAdvance = await repository.GetRecurringJobSeriesNeedingGenerationAsync(
            horizon,
            cancellationToken);
        if (seriesToAdvance.Count == 0)
        {
            return;
        }

        var occurrences = seriesToAdvance
            .SelectMany(series => series
                .GenerateOccurrencesThrough(horizon)
                .Select(occurrence => CreateOccurrence(series, occurrence)))
            .ToArray();

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
