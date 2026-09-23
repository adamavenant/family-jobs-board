using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.PointAdjustments;
using FamilyJobsBoard.Domain.Points;

namespace FamilyJobsBoard.Application.PointAdjustments;

public interface IPointAdjustmentRepository
{
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
    /// Persists pending changes. Throws <see cref="DuplicatePointAdjustmentRequestException"/>
    /// when the request ID was already used by a concurrent request.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
