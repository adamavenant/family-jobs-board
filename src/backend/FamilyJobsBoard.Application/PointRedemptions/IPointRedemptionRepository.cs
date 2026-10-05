using FamilyJobsBoard.Application.Points;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.PointRedemptions;
using FamilyJobsBoard.Domain.Points;

namespace FamilyJobsBoard.Application.PointRedemptions;

public interface IPointRedemptionRepository
{
    /// <summary>
    /// Starts a transaction holding the child's points lock. Reads and writes made before it
    /// is committed belong to that transaction.
    /// </summary>
    Task<IChildPointsLock> LockChildPointsAsync(Guid childId, CancellationToken cancellationToken);

    Task<HouseholdMember?> GetActiveChildAsync(Guid childId, CancellationToken cancellationToken);

    Task<PointRedemption?> GetRedemptionByRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken);

    Task AddRedemptionAsync(
        PointRedemption redemption,
        PointsLedgerEntry entry,
        CancellationToken cancellationToken);

    Task<int> GetPointsBalanceAsync(Guid childId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists pending changes. Throws <see cref="DuplicatePointRedemptionRequestException"/>
    /// when the request ID was already used by a concurrent request.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
