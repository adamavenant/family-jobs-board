using FamilyJobsBoard.Application.Today;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyJobsBoard.Infrastructure.Today;

public sealed class EfTodayBoardRepository : ITodayBoardRepository
{
    private readonly AppDbContext _database;

    public EfTodayBoardRepository(AppDbContext database)
    {
        _database = database;
    }

    public Task<HouseholdMember?> GetDemoChildAsync(CancellationToken cancellationToken)
    {
        return _database.HouseholdMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(member => member.Id == DemoDataIds.Child, cancellationToken);
    }

    public async Task<IReadOnlyList<Job>> GetJobsAsync(
        Guid childId,
        DateOnly scheduledDate,
        CancellationToken cancellationToken)
    {
        return await _database.Jobs
            .AsNoTracking()
            .Where(job => job.ChildId == childId && job.ScheduledDate == scheduledDate)
            .OrderBy(job => job.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<Job?> GetJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        return _database.Jobs.SingleOrDefaultAsync(job => job.Id == jobId, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _database.SaveChangesAsync(cancellationToken);
    }

    public async Task AddJobAsync(Job job, CancellationToken cancellationToken)
    {
        await _database.Jobs.AddAsync(job, cancellationToken);
    }
}
