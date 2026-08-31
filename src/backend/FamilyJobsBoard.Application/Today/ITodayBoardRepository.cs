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

    Task AddJobAsync(Job job, CancellationToken cancellationToken);

    Task<TodayPointsSummary> GetPointsSummaryAsync(
        Guid childId,
        CancellationToken cancellationToken);

    Task AddPointsAwardAsync(PointsLedgerEntry entry, CancellationToken cancellationToken);

    Task AddReviewDecisionAsync(
        JobReviewDecision decision,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
