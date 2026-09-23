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

    public async Task<TodayPointsSummary> GetPointsSummaryAsync(
        Guid childId,
        CancellationToken cancellationToken)
    {
        var entries = await _database.PointsLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.ChildId == childId)
            .OrderByDescending(entry => entry.AwardedAtUtc)
            .ThenByDescending(entry => entry.Id)
            .ToListAsync(cancellationToken);

        var jobIds = entries.Where(entry => entry.JobId is not null)
            .Select(entry => entry.JobId!.Value)
            .ToArray();
        var jobNames = await _database.Jobs
            .AsNoTracking()
            .Where(job => jobIds.Contains(job.Id))
            .ToDictionaryAsync(job => job.Id, job => job.Name, cancellationToken);

        var behaviourIds = entries.Where(entry => entry.GoodBehaviourId is not null)
            .Select(entry => entry.GoodBehaviourId!.Value)
            .ToArray();
        var behaviours = await _database.GoodBehaviours
            .AsNoTracking()
            .Where(behaviour => behaviourIds.Contains(behaviour.Id))
            .ToDictionaryAsync(behaviour => behaviour.Id, cancellationToken);
        var adjustmentIds = entries.Where(entry => entry.PointAdjustmentId is not null)
            .Select(entry => entry.PointAdjustmentId!.Value)
            .ToArray();
        var adjustments = await _database.PointAdjustments
            .AsNoTracking()
            .Where(adjustment => adjustmentIds.Contains(adjustment.Id))
            .ToDictionaryAsync(adjustment => adjustment.Id, cancellationToken);
        var adultIds = behaviours.Values
            .Select(behaviour => behaviour.LoggedByMemberId)
            .Concat(adjustments.Values.Select(adjustment => adjustment.AdjustedByMemberId))
            .Distinct()
            .ToArray();
        var adults = await _database.HouseholdMembers
            .AsNoTracking()
            .Where(member => adultIds.Contains(member.Id))
            .ToDictionaryAsync(member => member.Id, member => member.DisplayName, cancellationToken);

        var earnings = entries
            .Select(entry => entry switch
            {
                { GoodBehaviourId: { } behaviourId } => new TodayPointEarning(
                    entry.Id,
                    PointEarningSource.GoodBehaviour,
                    behaviours[behaviourId].TypeName,
                    null,
                    entry.Amount,
                    entry.AwardedAtUtc,
                    adults.GetValueOrDefault(behaviours[behaviourId].LoggedByMemberId)),
                { PointAdjustmentId: { } adjustmentId } => new TodayPointEarning(
                    entry.Id,
                    PointEarningSource.ManualAdjustment,
                    adjustments[adjustmentId].Reason,
                    null,
                    entry.Amount,
                    entry.AwardedAtUtc,
                    adults.GetValueOrDefault(adjustments[adjustmentId].AdjustedByMemberId)),
                _ => new TodayPointEarning(
                    entry.Id,
                    PointEarningSource.Job,
                    jobNames[entry.JobId!.Value],
                    entry.JobId,
                    entry.Amount,
                    entry.AwardedAtUtc,
                    null),
            })
            .ToArray();

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
