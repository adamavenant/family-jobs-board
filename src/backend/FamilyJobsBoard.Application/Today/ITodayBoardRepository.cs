using FamilyJobsBoard.Application.Points;
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

    /// <summary>The job's assigned child, read without tracking the job.</summary>
    Task<Guid?> GetJobChildIdAsync(Guid jobId, CancellationToken cancellationToken);

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

    Task<IReadOnlyList<RecurringJobSlot>> GetRecurringJobSlotsAsync(
        IReadOnlyCollection<Guid> seriesIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RecurringJobSeries>> GetRecurringJobSeriesAsync(
        IReadOnlyCollection<Guid> seriesIds,
        CancellationToken cancellationToken);

    Task AddRecurringJobSeriesAsync(
        IReadOnlyCollection<RecurringJobSeries> series,
        CancellationToken cancellationToken);

    Task<int> GetRecurringJobSeriesOccurrenceCountAsync(
        Guid seriesId,
        CancellationToken cancellationToken);

    Task<int> GetPointsBalanceAsync(Guid childId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a job's ledger entry: an approval's award or the reversal that takes it back.
    /// </summary>
    Task AddPointsAwardAsync(PointsLedgerEntry entry, CancellationToken cancellationToken);

    /// <summary>The job's award for its <paramref name="approvalNumber"/>th approval, if any.</summary>
    Task<PointsLedgerEntry?> GetJobAwardAsync(
        Guid jobId,
        int approvalNumber,
        CancellationToken cancellationToken);

    /// <summary>
    /// Starts a transaction holding the child's points lock, which every use case that removes
    /// points takes. Reads and writes made before it is committed belong to that transaction.
    /// </summary>
    Task<IChildPointsLock> LockChildPointsAsync(Guid childId, CancellationToken cancellationToken);

    Task AddReviewDecisionAsync(
        JobReviewDecision decision,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists pending changes. Throws <see cref="JobStateConflictException"/> when a job
    /// changed concurrently, <see cref="DuplicateJobPointsAwardException"/> when the approval
    /// was already awarded, and <see cref="DuplicateJobApprovalReversalException"/> when the
    /// award was already reversed.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
