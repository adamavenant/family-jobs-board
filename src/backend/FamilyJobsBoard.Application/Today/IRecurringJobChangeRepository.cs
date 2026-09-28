using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;

namespace FamilyJobsBoard.Application.Today;

public interface IRecurringJobChangeRepository
{
    Task<HouseholdMember?> GetMemberAsync(Guid memberId, CancellationToken cancellationToken);

    Task<Job?> GetJobAsync(Guid jobId, bool tracking, CancellationToken cancellationToken);

    Task<RecurringJobSeries?> GetSeriesAsync(
        Guid seriesId,
        bool tracking,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Job>> GetSeriesJobsAsync(
        Guid seriesId,
        bool tracking,
        CancellationToken cancellationToken);

    Task<RecurringJobChange?> GetChangeAsync(Guid requestId, CancellationToken cancellationToken);

    Task AddJobsAsync(IReadOnlyCollection<Job> jobs, CancellationToken cancellationToken);

    Task AddChangeAsync(RecurringJobChange change, CancellationToken cancellationToken);

    Task AddRevisionAsync(
        RecurringJobSeriesRevision revision,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
