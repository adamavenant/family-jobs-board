using System.Linq.Expressions;
using FamilyJobsBoard.Domain.Points;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyJobsBoard.Infrastructure.Points;

internal static class LedgerBalances
{
    /// <summary>
    /// The child's balance counting every entry at or before the matching entry, in the
    /// ledger's (awarded_at_utc, id) order, so it matches the ledger's balance-after.
    /// </summary>
    public static async Task<int> AfterEntryAsync(
        AppDbContext database,
        Expression<Func<PointsLedgerEntry, bool>> entryFilter,
        CancellationToken cancellationToken)
    {
        var entry = await database.PointsLedgerEntries
            .AsNoTracking()
            .Where(entryFilter)
            .Select(item => new { item.ChildId, item.AwardedAtUtc, item.Id })
            .SingleAsync(cancellationToken);
        return await database.PointsLedgerEntries
            .AsNoTracking()
            .Where(item => item.ChildId == entry.ChildId
                && EF.Functions.LessThanOrEqual(
                    ValueTuple.Create(item.AwardedAtUtc, item.Id),
                    ValueTuple.Create(entry.AwardedAtUtc, entry.Id)))
            .SumAsync(item => item.Amount, cancellationToken);
    }
}
