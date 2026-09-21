using FamilyJobsBoard.Domain.GoodBehaviours;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Points;

namespace FamilyJobsBoard.Application.GoodBehaviours;

public interface IGoodBehaviourRepository
{
    Task<IReadOnlyList<GoodBehaviourType>> GetActiveTypesAsync(CancellationToken cancellationToken);

    Task<GoodBehaviourType?> GetTypeAsync(Guid typeId, CancellationToken cancellationToken);

    Task AddTypeAsync(GoodBehaviourType type, CancellationToken cancellationToken);

    Task<HouseholdMember?> GetActiveChildAsync(Guid childId, CancellationToken cancellationToken);

    Task<GoodBehaviour?> GetBehaviourByRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken);

    Task AddBehaviourAsync(
        GoodBehaviour behaviour,
        PointsLedgerEntry award,
        CancellationToken cancellationToken);

    Task<int> GetPointsBalanceAsync(Guid childId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists pending changes. Throws <see cref="DuplicateGoodBehaviourRequestException"/>
    /// when the request ID was already used by a concurrent request.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
