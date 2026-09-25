using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.TurnRotations;

namespace FamilyJobsBoard.Application.TurnRotations;

public interface ITurnRotationRepository
{
    Task<IReadOnlyList<TurnRotationRevision>> GetRevisionsAsync(CancellationToken cancellationToken);

    Task<HouseholdMember?> GetMemberAsync(Guid memberId, CancellationToken cancellationToken);

    Task<IReadOnlyList<HouseholdMember>> GetActiveChildrenAsync(
        IReadOnlyCollection<Guid> childIds,
        CancellationToken cancellationToken);

    Task AddRevisionAsync(TurnRotationRevision revision, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
