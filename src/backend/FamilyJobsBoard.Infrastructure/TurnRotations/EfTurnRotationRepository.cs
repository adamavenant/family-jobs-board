using FamilyJobsBoard.Application.TurnRotations;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.TurnRotations;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FamilyJobsBoard.Infrastructure.TurnRotations;

public sealed class EfTurnRotationRepository : ITurnRotationRepository
{
    private readonly AppDbContext _database;

    public EfTurnRotationRepository(AppDbContext database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<TurnRotationRevision>> GetRevisionsAsync(
        CancellationToken cancellationToken)
    {
        return await _database.TurnRotationRevisions
            .AsNoTracking()
            .OrderBy(revision => revision.EffectiveFrom)
            .ThenBy(revision => revision.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TakenTurn>> GetTakenTurnsAsync(
        IReadOnlyCollection<Guid> revisionIds,
        CancellationToken cancellationToken)
    {
        return await _database.TakenTurns
            .AsNoTracking()
            .Where(turn => revisionIds.Contains(turn.RevisionId) && turn.UndoneAtUtc == null)
            .OrderBy(turn => turn.RecordedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public Task<TakenTurn?> FindTakenTurnAsync(Guid takenTurnId, CancellationToken cancellationToken)
    {
        return _database.TakenTurns.SingleOrDefaultAsync(
            turn => turn.Id == takenTurnId,
            cancellationToken);
    }

    public Task<TakenTurn?> GetTakenTurnByRequestAsync(Guid requestId, CancellationToken cancellationToken)
    {
        return _database.TakenTurns
            .AsNoTracking()
            .SingleOrDefaultAsync(turn => turn.RequestId == requestId, cancellationToken);
    }

    public Task<HouseholdMember?> GetMemberAsync(Guid memberId, CancellationToken cancellationToken)
    {
        return _database.HouseholdMembers.SingleOrDefaultAsync(
            member => member.Id == memberId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<HouseholdMember>> GetMembersAsync(
        IReadOnlyCollection<Guid> memberIds,
        CancellationToken cancellationToken)
    {
        return await _database.HouseholdMembers
            .AsNoTracking()
            .Where(member => memberIds.Contains(member.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HouseholdMember>> GetActiveChildrenAsync(
        IReadOnlyCollection<Guid> childIds,
        CancellationToken cancellationToken)
    {
        return await _database.HouseholdMembers
            .AsNoTracking()
            .Where(member => childIds.Contains(member.Id)
                && member.IsActive
                && member.Role == HouseholdRole.Child)
            .OrderBy(member => member.FirstName)
            .ThenBy(member => member.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<ITurnRotationLock> LockRotationAsync(Guid rotationId, CancellationToken cancellationToken)
    {
        return EfTurnRotationLock.AcquireAsync(_database, rotationId, cancellationToken);
    }

    public async Task AddRevisionAsync(TurnRotationRevision revision, CancellationToken cancellationToken)
    {
        await _database.TurnRotationRevisions.AddAsync(revision, cancellationToken);
    }

    public async Task AddTakenTurnAsync(TakenTurn takenTurn, CancellationToken cancellationToken)
    {
        await _database.TakenTurns.AddAsync(takenTurn, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: TakenTurnConfiguration.RequestIdIndexName
                    or TakenTurnConfiguration.CountedTurnIndexName,
            } violation)
        {
            // The rotation lock makes these unreachable for one rotation; they guard a request
            // ID reused across rotations and any tick that bypassed the lock.
            _database.ChangeTracker.Clear();
            throw new TakenTurnConflictException(
                violation.ConstraintName == TakenTurnConfiguration.RequestIdIndexName
                    ? "That request ID was already used for a different tick."
                    : "That turn has already moved on.");
        }
    }
}
