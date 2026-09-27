using FamilyJobsBoard.Application.TurnRotations;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.TurnRotations;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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

    public async Task AddRevisionAsync(TurnRotationRevision revision, CancellationToken cancellationToken)
    {
        await _database.TurnRotationRevisions.AddAsync(revision, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _database.SaveChangesAsync(cancellationToken);
    }
}
