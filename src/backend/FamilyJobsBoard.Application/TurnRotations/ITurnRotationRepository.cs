using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.TurnRotations;

namespace FamilyJobsBoard.Application.TurnRotations;

public interface ITurnRotationRepository
{
    Task<IReadOnlyList<TurnRotationRevision>> GetRevisionsAsync(CancellationToken cancellationToken);

    /// <summary>The ticks of the given revisions that have not been unticked.</summary>
    Task<IReadOnlyList<TakenTurn>> GetTakenTurnsAsync(
        IReadOnlyCollection<Guid> revisionIds,
        CancellationToken cancellationToken);

    /// <summary>A tick, tracked for unticking, whether or not it has already been unticked.</summary>
    Task<TakenTurn?> FindTakenTurnAsync(Guid takenTurnId, CancellationToken cancellationToken);

    /// <summary>The tick recorded by a request, whether or not it has since been unticked.</summary>
    Task<TakenTurn?> GetTakenTurnByRequestAsync(Guid requestId, CancellationToken cancellationToken);

    Task<HouseholdMember?> GetMemberAsync(Guid memberId, CancellationToken cancellationToken);

    Task<IReadOnlyList<HouseholdMember>> GetMembersAsync(
        IReadOnlyCollection<Guid> memberIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<HouseholdMember>> GetActiveChildrenAsync(
        IReadOnlyCollection<Guid> childIds,
        CancellationToken cancellationToken);

    Task<ITurnRotationLock> LockRotationAsync(Guid rotationId, CancellationToken cancellationToken);

    Task AddRevisionAsync(TurnRotationRevision revision, CancellationToken cancellationToken);

    Task AddTakenTurnAsync(TakenTurn takenTurn, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
