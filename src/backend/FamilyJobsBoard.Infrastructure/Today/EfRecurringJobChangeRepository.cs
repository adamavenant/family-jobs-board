using FamilyJobsBoard.Application.Today;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FamilyJobsBoard.Infrastructure.Today;

public sealed class EfRecurringJobChangeRepository : IRecurringJobChangeRepository
{
    private readonly AppDbContext _database;

    public EfRecurringJobChangeRepository(AppDbContext database)
    {
        _database = database;
    }

    public Task<HouseholdMember?> GetMemberAsync(Guid memberId, CancellationToken cancellationToken) =>
        _database.HouseholdMembers.SingleOrDefaultAsync(
            member => member.Id == memberId && member.IsActive,
            cancellationToken);

    public Task<Job?> GetJobAsync(Guid jobId, bool tracking, CancellationToken cancellationToken)
    {
        var query = tracking ? _database.Jobs : _database.Jobs.AsNoTracking();
        return query.SingleOrDefaultAsync(job => job.Id == jobId, cancellationToken);
    }

    public Task<RecurringJobSeries?> GetSeriesAsync(
        Guid seriesId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var query = tracking
            ? _database.RecurringJobSeries
            : _database.RecurringJobSeries.AsNoTracking();
        return query.SingleOrDefaultAsync(series => series.Id == seriesId, cancellationToken);
    }

    public async Task<IReadOnlyList<Job>> GetSeriesJobsAsync(
        Guid seriesId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var query = tracking ? _database.Jobs : _database.Jobs.AsNoTracking();
        return await query.Where(job => job.RecurringJobSeriesId == seriesId)
            .OrderBy(job => job.ScheduledDate)
            .ToListAsync(cancellationToken);
    }

    public Task<RecurringJobChange?> GetChangeAsync(
        Guid requestId,
        CancellationToken cancellationToken) =>
        _database.RecurringJobChanges.AsNoTracking()
            .SingleOrDefaultAsync(change => change.Id == requestId, cancellationToken);

    public async Task AddJobsAsync(
        IReadOnlyCollection<Job> jobs,
        CancellationToken cancellationToken) =>
        await _database.Jobs.AddRangeAsync(jobs, cancellationToken);

    public async Task AddChangeAsync(
        RecurringJobChange change,
        CancellationToken cancellationToken) =>
        await _database.RecurringJobChanges.AddAsync(change, cancellationToken);

    public async Task AddRevisionAsync(
        RecurringJobSeriesRevision revision,
        CancellationToken cancellationToken) =>
        await _database.RecurringJobSeriesRevisions.AddAsync(revision, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _database.ChangeTracker.Clear();
            throw new RecurringJobChangeConcurrentApplyException(exception);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "PK_recurring_job_changes",
            })
        {
            _database.ChangeTracker.Clear();
            throw new RecurringJobChangeConcurrentApplyException(exception);
        }
    }
}
