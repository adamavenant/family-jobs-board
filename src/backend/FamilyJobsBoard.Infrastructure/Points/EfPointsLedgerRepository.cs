using FamilyJobsBoard.Application.Points;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyJobsBoard.Infrastructure.Points;

public sealed class EfPointsLedgerRepository : IPointsLedgerRepository
{
    private readonly AppDbContext _database;

    public EfPointsLedgerRepository(AppDbContext database)
    {
        _database = database;
    }

    public Task<HouseholdMember?> GetActiveMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        return _database.HouseholdMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                member => member.Id == memberId && member.IsActive,
                cancellationToken);
    }

    public async Task<IReadOnlyList<HouseholdMember>> GetChildrenAsync(
        CancellationToken cancellationToken)
    {
        return await _database.HouseholdMembers
            .AsNoTracking()
            .Where(member => member.Role == HouseholdRole.Child)
            .OrderBy(member => member.FirstName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetBalancesAsync(
        CancellationToken cancellationToken)
    {
        return await _database.PointsLedgerEntries
            .AsNoTracking()
            .GroupBy(entry => entry.ChildId)
            .Select(group => new { ChildId = group.Key, Balance = group.Sum(entry => entry.Amount) })
            .ToDictionaryAsync(item => item.ChildId, item => item.Balance, cancellationToken);
    }

    public async Task<IReadOnlyList<PointsLedgerRecord>> GetEntriesAsync(
        Guid? childId,
        PointsLedgerPosition? before,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _database.PointsLedgerEntries.AsNoTracking();
        if (childId is { } selectedChildId)
        {
            query = query.Where(entry => entry.ChildId == selectedChildId);
        }

        if (before is { } position)
        {
            var awardedAtUtc = position.AwardedAtUtc;
            var entryId = position.Id;
            // A PostgreSQL row comparison, so paging follows the database's own uuid ordering.
            query = query.Where(entry => EF.Functions.LessThan(
                ValueTuple.Create(entry.AwardedAtUtc, entry.Id),
                ValueTuple.Create(awardedAtUtc, entryId)));
        }

        var entries = await query
            .OrderByDescending(entry => entry.AwardedAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Take(take)
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
            .ToDictionaryAsync(
                behaviour => behaviour.Id,
                behaviour => new RecordedSource(behaviour.TypeName, behaviour.LoggedByMemberId),
                cancellationToken);
        var adjustmentIds = entries.Where(entry => entry.PointAdjustmentId is not null)
            .Select(entry => entry.PointAdjustmentId!.Value)
            .ToArray();
        var adjustments = await _database.PointAdjustments
            .AsNoTracking()
            .Where(adjustment => adjustmentIds.Contains(adjustment.Id))
            .ToDictionaryAsync(
                adjustment => adjustment.Id,
                adjustment => new RecordedSource(adjustment.Reason, adjustment.AdjustedByMemberId),
                cancellationToken);
        var redemptionIds = entries.Where(entry => entry.PointRedemptionId is not null)
            .Select(entry => entry.PointRedemptionId!.Value)
            .ToArray();
        var redemptions = await _database.PointRedemptions
            .AsNoTracking()
            .Where(redemption => redemptionIds.Contains(redemption.Id))
            .ToDictionaryAsync(
                redemption => redemption.Id,
                redemption => new RecordedSource(redemption.Reward, redemption.RedeemedByMemberId),
                cancellationToken);

        // Job approvals don't record the deciding adult yet, so job awards have no recorder.
        var sources = entries.ToDictionary(
            entry => entry.Id,
            entry => entry switch
            {
                { GoodBehaviourId: { } behaviourId } => behaviours[behaviourId],
                { PointAdjustmentId: { } adjustmentId } => adjustments[adjustmentId],
                { PointRedemptionId: { } redemptionId } => redemptions[redemptionId],
                _ => new RecordedSource(jobNames[entry.JobId!.Value], null),
            });
        var recorderIds = sources.Values
            .Where(source => source.RecordedByMemberId is not null)
            .Select(source => source.RecordedByMemberId!.Value)
            .Distinct()
            .ToArray();
        // Recorders are listed even after they are deactivated, so history keeps their name.
        var recorderNames = (await _database.HouseholdMembers
            .AsNoTracking()
            .Where(member => recorderIds.Contains(member.Id))
            .ToListAsync(cancellationToken))
            .ToDictionary(member => member.Id, member => member.DisplayName);

        return entries
            .Select(entry =>
            {
                var source = sources[entry.Id];
                return new PointsLedgerRecord(
                    entry.Id,
                    entry.ChildId,
                    source.Name,
                    entry.Amount,
                    entry.AwardedAtUtc,
                    source.RecordedByMemberId is { } recorderId
                        ? recorderNames.GetValueOrDefault(recorderId)
                        : null);
            })
            .ToArray();
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetBalancesThroughAsync(
        IReadOnlyCollection<Guid> childIds,
        PointsLedgerPosition through,
        CancellationToken cancellationToken)
    {
        var awardedAtUtc = through.AwardedAtUtc;
        var entryId = through.Id;
        return await _database.PointsLedgerEntries
            .AsNoTracking()
            .Where(entry => childIds.Contains(entry.ChildId)
                && EF.Functions.LessThanOrEqual(
                    ValueTuple.Create(entry.AwardedAtUtc, entry.Id),
                    ValueTuple.Create(awardedAtUtc, entryId)))
            .GroupBy(entry => entry.ChildId)
            .Select(group => new { ChildId = group.Key, Balance = group.Sum(entry => entry.Amount) })
            .ToDictionaryAsync(item => item.ChildId, item => item.Balance, cancellationToken);
    }

    private sealed record RecordedSource(string Name, Guid? RecordedByMemberId);
}
