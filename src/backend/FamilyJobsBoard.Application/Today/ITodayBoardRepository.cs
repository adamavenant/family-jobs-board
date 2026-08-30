using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;

namespace FamilyJobsBoard.Application.Today;

public interface ITodayBoardRepository
{
    Task<HouseholdMember?> GetDemoChildAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Job>> GetJobsAsync(
        Guid childId,
        DateOnly scheduledDate,
        CancellationToken cancellationToken);

    Task<Job?> GetJobAsync(Guid jobId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
    
    Task AddJobAsync(Guid childId, Job job, CancellationToken cancellationToken);
}
