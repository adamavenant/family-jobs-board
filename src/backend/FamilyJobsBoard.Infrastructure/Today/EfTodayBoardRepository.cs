using FamilyJobsBoard.Application.Today;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Domain.Points;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FamilyJobsBoard.Infrastructure.Today;

public sealed class EfTodayBoardRepository : ITodayBoardRepository
{
    private readonly AppDbContext _database;

    public EfTodayBoardRepository(AppDbContext database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<HouseholdMember>> GetMembersAsync(
        CancellationToken cancellationToken)
    {
        return await _database.HouseholdMembers
            .AsNoTracking()
            .OrderByDescending(member => member.IsAdult)
            .ThenBy(member => member.FirstName)
            .ToListAsync(cancellationToken);
    }

    public Task<HouseholdMember?> GetMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        return _database.HouseholdMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(member => member.Id == memberId, cancellationToken);
    }

    public async Task<IReadOnlyList<Job>> GetJobsAsync(
        IReadOnlyCollection<Guid> childIds,
        DateOnly scheduledDate,
        CancellationToken cancellationToken)
    {
        return await _database.Jobs
            .AsNoTracking()
            .Where(job => childIds.Contains(job.ChildId) && job.ScheduledDate == scheduledDate)
            .OrderBy(job => job.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<Job?> GetJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        return _database.Jobs.SingleOrDefaultAsync(job => job.Id == jobId, cancellationToken);
    }

    public async Task<IReadOnlyList<TodayJobRejection>> GetLatestRejectionsAsync(
        IReadOnlyCollection<Guid> childIds,
        DateOnly scheduledDate,
        CancellationToken cancellationToken)
    {
        var rejections = await _database.JobReviewDecisions
            .AsNoTracking()
            .Where(decision => decision.Outcome == JobReviewOutcome.Rejected)
            .Join(
                _database.Jobs.AsNoTracking().Where(job =>
                    childIds.Contains(job.ChildId) && job.ScheduledDate == scheduledDate),
                decision => decision.JobId,
                job => job.Id,
                (decision, _) => decision)
            .OrderByDescending(decision => decision.DecidedAtUtc)
            .ThenByDescending(decision => decision.Id)
            .Select(decision => new TodayJobRejection(
                decision.Id,
                decision.JobId,
                decision.Reason,
                decision.DecidedAtUtc))
            .ToListAsync(cancellationToken);

        return rejections
            .GroupBy(rejection => rejection.JobId)
            .Select(group => group.First())
            .ToArray();
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ux_points_ledger_entries_job_id",
            })
        {
            throw new DuplicateJobPointsAwardException();
        }
    }

    public async Task AddJobAsync(Job job, CancellationToken cancellationToken)
    {
        await _database.Jobs.AddAsync(job, cancellationToken);
    }

    public async Task AddJobsAsync(
        IReadOnlyCollection<Job> jobs,
        CancellationToken cancellationToken)
    {
        await _database.Jobs.AddRangeAsync(jobs, cancellationToken);
    }

    public Task<DailyJobSeries?> GetDailyJobSeriesAsync(
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        return _database.DailyJobSeries
            .AsNoTracking()
            .SingleOrDefaultAsync(series => series.Id == seriesId, cancellationToken);
    }

    public async Task<IReadOnlyList<DailyJobSeries>> GetDailyJobSeriesNeedingGenerationAsync(
        DateOnly horizon,
        CancellationToken cancellationToken)
    {
        return await _database.DailyJobSeries
            .Where(series =>
                series.GeneratedThrough < horizon
                && (series.EndDate == null || series.GeneratedThrough < series.EndDate))
            .ToListAsync(cancellationToken);
    }

    public async Task AddDailyJobSeriesAsync(
        DailyJobSeries series,
        CancellationToken cancellationToken)
    {
        await _database.DailyJobSeries.AddAsync(series, cancellationToken);
    }

    public Task<int> GetDailyJobSeriesOccurrenceCountAsync(
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        return _database.Jobs.CountAsync(
            job => job.RecurringJobSeriesId == seriesId,
            cancellationToken);
    }

    public async Task<TodayPointsSummary> GetPointsSummaryAsync(
        Guid childId,
        CancellationToken cancellationToken)
    {
        var earnings = await _database.PointsLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.ChildId == childId)
            .Join(
                _database.Jobs.AsNoTracking(),
                entry => entry.JobId,
                job => job.Id,
                (entry, job) => new { Entry = entry, JobName = job.Name })
            .OrderByDescending(result => result.Entry.AwardedAtUtc)
            .ThenByDescending(result => result.Entry.Id)
            .Select(result => new TodayPointEarning(
                result.Entry.Id,
                result.Entry.JobId,
                result.JobName,
                result.Entry.Amount,
                result.Entry.AwardedAtUtc))
            .ToListAsync(cancellationToken);

        return new TodayPointsSummary(
            earnings.Sum(earning => earning.Points),
            earnings);
    }

    public async Task AddPointsAwardAsync(
        PointsLedgerEntry entry,
        CancellationToken cancellationToken)
    {
        await _database.PointsLedgerEntries.AddAsync(entry, cancellationToken);
    }

    public async Task AddReviewDecisionAsync(
        JobReviewDecision decision,
        CancellationToken cancellationToken)
    {
        await _database.JobReviewDecisions.AddAsync(decision, cancellationToken);
    }
}
