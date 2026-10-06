using FamilyJobsBoard.Application.Points;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.PointAdjustments;
using FamilyJobsBoard.Domain.Points;

namespace FamilyJobsBoard.Application.PointAdjustments;

public interface IPointAdjustmentRepository
{
    /// <summary>
    /// Starts a transaction holding the child's points lock. Reads and writes made before it
    /// is committed belong to that transaction.
    /// </summary>
    Task<IChildPointsLock> LockChildPointsAsync(Guid childId, CancellationToken cancellationToken);

    Task<HouseholdMember?> GetActiveChildAsync(Guid childId, CancellationToken cancellationToken);

    Task<PointAdjustment?> GetAdjustmentByRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken);

    Task AddAdjustmentAsync(
        PointAdjustment adjustment,
        PointsLedgerEntry entry,
        CancellationToken cancellationToken);

    Task<int> GetPointsBalanceAsync(Guid childId, CancellationToken cancellationToken);

    /// <summary>
    /// The child's balance right after the adjustment's ledger entry, in ledger order: the
    /// balance-after the points ledger shows for that entry, however many entries followed.
    /// </summary>
    Task<int> GetPointsBalanceAfterAdjustmentAsync(
        Guid adjustmentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists pending changes. Throws <see cref="DuplicatePointAdjustmentRequestException"/>
    /// when the request ID was already used by a concurrent request.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
