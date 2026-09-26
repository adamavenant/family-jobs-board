using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Domain.Points;

namespace FamilyJobsBoard.Application.Today;

public interface ITodayBoardRepository
{
    Task<IReadOnlyList<HouseholdMember>> GetMembersAsync(CancellationToken cancellationToken);

    Task<HouseholdMember?> GetMemberAsync(Guid memberId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Job>> GetJobsAsync(
        IReadOnlyCollection<Guid> childIds,
        DateOnly scheduledDate,
        CancellationToken cancellationToken);

    Task<Job?> GetJobAsync(Guid jobId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TodayJobRejection>> GetLatestRejectionsAsync(
        IReadOnlyCollection<Guid> childIds,
        DateOnly scheduledDate,
        CancellationToken cancellationToken);

    /// <summary>
    /// Jobs scheduled anywhere in [startDate, endDate], ordered the same way as the daily
    /// agenda within each date, for the calendar's day/week/month views.
    /// </summary>
    Task<IReadOnlyList<Job>> GetJobsInRangeAsync(
        IReadOnlyCollection<Guid> childIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TodayJobRejection>> GetLatestRejectionsInRangeAsync(
        IReadOnlyCollection<Guid> childIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken);

    Task AddJobsAsync(IReadOnlyCollection<Job> jobs, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecurringJobSeries>> GetRecurringJobSeriesByRequestAsync(
        Guid assignmentRequestId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RecurringJobSeries>> GetRecurringJobSeriesNeedingGenerationAsync(
        DateOnly horizon,
        CancellationToken cancellationToken);

    Task AddRecurringJobSeriesAsync(
        IReadOnlyCollection<RecurringJobSeries> series,
        CancellationToken cancellationToken);

    Task<int> GetRecurringJobSeriesOccurrenceCountAsync(
        Guid seriesId,
        CancellationToken cancellationToken);

    Task<TodayPointsSummary> GetPointsSummaryAsync(
        Guid childId,
        CancellationToken cancellationToken);

    Task AddPointsAwardAsync(PointsLedgerEntry entry, CancellationToken cancellationToken);

    Task AddReviewDecisionAsync(
        JobReviewDecision decision,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
