using FamilyJobsBoard.Domain.GoodBehaviours;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Points;

namespace FamilyJobsBoard.Application.GoodBehaviours;

public interface IGoodBehaviourRepository
{
    Task<IReadOnlyList<GoodBehaviourType>> GetActiveTypesAsync(CancellationToken cancellationToken);

    Task<GoodBehaviourType?> GetTypeAsync(Guid typeId, CancellationToken cancellationToken);

    Task AddTypeAsync(GoodBehaviourType type, CancellationToken cancellationToken);

    Task<IReadOnlyList<HouseholdMember>> GetActiveChildrenAsync(
        IReadOnlyCollection<Guid> childIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GoodBehaviour>> GetBehavioursByRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken);

    Task AddBehavioursAsync(
        IReadOnlyCollection<(GoodBehaviour Behaviour, PointsLedgerEntry Award)> entries,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, int>> GetPointsBalancesAsync(
        IReadOnlyCollection<Guid> childIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists pending changes. Throws <see cref="DuplicateGoodBehaviourRequestException"/>
    /// when the request ID was already used for one of the children by a concurrent request.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
