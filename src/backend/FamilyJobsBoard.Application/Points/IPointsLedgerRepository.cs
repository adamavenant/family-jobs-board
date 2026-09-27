using FamilyJobsBoard.Domain.Households;

namespace FamilyJobsBoard.Application.Points;

public interface IPointsLedgerRepository
{
    Task<HouseholdMember?> GetActiveMemberAsync(Guid memberId, CancellationToken cancellationToken);

    /// <summary>Every child in the household, active or deactivated.</summary>
    Task<IReadOnlyList<HouseholdMember>> GetChildrenAsync(CancellationToken cancellationToken);

    /// <summary>Current balance of each child that has at least one ledger entry.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetBalancesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Entries newest first, optionally for one child and strictly older than
    /// <paramref name="before"/>.
    /// </summary>
    Task<IReadOnlyList<PointsLedgerRecord>> GetEntriesAsync(
        Guid? childId,
        PointsLedgerPosition? before,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Each listed child's balance counting only entries at or before <paramref name="through"/>.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> GetBalancesThroughAsync(
        IReadOnlyCollection<Guid> childIds,
        PointsLedgerPosition through,
        CancellationToken cancellationToken);
}
