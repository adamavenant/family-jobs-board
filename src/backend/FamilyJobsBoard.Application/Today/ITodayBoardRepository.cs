using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Domain.Points;

namespace FamilyJobsBoard.Application.Today;

public interface ITodayBoardRepository
{
    Task<HouseholdMember?> GetDemoChildAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Job>> GetJobsAsync(
        Guid childId,
        DateOnly scheduledDate,
        CancellationToken cancellationToken);

    Task<Job?> GetJobAsync(Guid jobId, CancellationToken cancellationToken);

    Task AddJobAsync(Job job, CancellationToken cancellationToken);

    Task<int> GetPointsBalanceAsync(Guid childId, CancellationToken cancellationToken);

    Task AddPointsAwardAsync(PointsLedgerEntry entry, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
