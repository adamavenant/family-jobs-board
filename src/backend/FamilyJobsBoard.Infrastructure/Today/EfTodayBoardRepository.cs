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
            .Where(member => member.IsActive)
            .OrderByDescending(member => member.Role == HouseholdRole.Adult)
            .ThenBy(member => member.FirstName)
            .ToListAsync(cancellationToken);
    }

    public Task<HouseholdMember?> GetMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        return _database.HouseholdMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                member => member.Id == memberId && member.IsActive,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Job>> GetJobsAsync(
        IReadOnlyCollection<Guid> childIds,
        DateOnly scheduledDate,
        CancellationToken cancellationToken)
    {
        return await _database.Jobs
            .AsNoTracking()
            .Where(job =>
                childIds.Contains(job.ChildId)
                && job.ScheduledDate == scheduledDate
                && job.Status != JobStatus.Cancelled)
            .OrderBy(job => job.AgendaPeriod)
            .ThenBy(job => job.ScheduledTime == null)
            .ThenBy(job => job.ScheduledTime)
            .ThenBy(job => job.Name)
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

    public async Task<IReadOnlyList<Job>> GetJobsInRangeAsync(
        IReadOnlyCollection<Guid> childIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        return await _database.Jobs
            .AsNoTracking()
            .Where(job => childIds.Contains(job.ChildId)
                && job.ScheduledDate >= startDate
                && job.ScheduledDate <= endDate
                && job.Status != JobStatus.Cancelled)
            .OrderBy(job => job.ScheduledDate)
            .ThenBy(job => job.AgendaPeriod)
            .ThenBy(job => job.ScheduledTime == null)
            .ThenBy(job => job.ScheduledTime)
            .ThenBy(job => job.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TodayJobRejection>> GetLatestRejectionsInRangeAsync(
        IReadOnlyCollection<Guid> childIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        var rejections = await _database.JobReviewDecisions
            .AsNoTracking()
            .Where(decision => decision.Outcome == JobReviewOutcome.Rejected)
            .Join(
                _database.Jobs.AsNoTracking().Where(job =>
                    childIds.Contains(job.ChildId)
                    && job.ScheduledDate >= startDate
                    && job.ScheduledDate <= endDate),
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
        catch (DbUpdateConcurrencyException exception)
        {
            _database.ChangeTracker.Clear();
            if (exception.Entries.Any(entry => entry.Entity is Job))
            {
                throw new JobStateConflictException(exception);
            }

            throw new RecurringJobGenerationConflictException(exception);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ux_jobs_recurring_series_date",
            })
        {
            _database.ChangeTracker.Clear();
            throw new RecurringJobGenerationConflictException(exception);
        }
    }

    public async Task AddJobsAsync(
        IReadOnlyCollection<Job> jobs,
        CancellationToken cancellationToken)
    {
        await _database.Jobs.AddRangeAsync(jobs, cancellationToken);
    }

    public async Task<IReadOnlyList<RecurringJobSeries>> GetRecurringJobSeriesByRequestAsync(
        Guid assignmentRequestId,
        CancellationToken cancellationToken)
    {
        return await _database.RecurringJobSeries
            .AsNoTracking()
            .Where(series => series.AssignmentRequestId == assignmentRequestId)
            .OrderBy(series => series.ChildId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RecurringJobSeries>> GetRecurringJobSeriesNeedingGenerationAsync(
        DateOnly horizon,
        CancellationToken cancellationToken)
    {
        return await _database.RecurringJobSeries
            .Where(series =>
                series.GeneratedThrough < horizon
                && (series.EndDate == null || series.GeneratedThrough < series.EndDate))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RecurringJobSlot>> GetRecurringJobSlotsAsync(
        IReadOnlyCollection<Guid> seriesIds,
        CancellationToken cancellationToken)
    {
        return await _database.Jobs
            .AsNoTracking()
            .Where(job => job.RecurringJobSeriesId != null
                && seriesIds.Contains(job.RecurringJobSeriesId.Value))
            .Select(job => new RecurringJobSlot(
                job.RecurringJobSeriesId!.Value,
                job.ScheduledDate,
                job.OriginalScheduledDate ?? job.ScheduledDate))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RecurringJobSeries>> GetRecurringJobSeriesAsync(
        IReadOnlyCollection<Guid> seriesIds,
        CancellationToken cancellationToken)
    {
        return await _database.RecurringJobSeries
            .AsNoTracking()
            .Where(series => seriesIds.Contains(series.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task AddRecurringJobSeriesAsync(
        IReadOnlyCollection<RecurringJobSeries> series,
        CancellationToken cancellationToken)
    {
        await _database.RecurringJobSeries.AddRangeAsync(series, cancellationToken);
    }

    public Task<int> GetRecurringJobSeriesOccurrenceCountAsync(
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        return _database.Jobs.CountAsync(
            job => job.RecurringJobSeriesId == seriesId,
            cancellationToken);
    }

    public Task<int> GetPointsBalanceAsync(Guid childId, CancellationToken cancellationToken)
    {
        return _database.PointsLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.ChildId == childId)
            .SumAsync(entry => entry.Amount, cancellationToken);
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
