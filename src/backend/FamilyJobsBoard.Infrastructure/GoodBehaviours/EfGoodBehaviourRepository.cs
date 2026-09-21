using FamilyJobsBoard.Application.GoodBehaviours;
using FamilyJobsBoard.Domain.GoodBehaviours;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Points;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FamilyJobsBoard.Infrastructure.GoodBehaviours;

public sealed class EfGoodBehaviourRepository : IGoodBehaviourRepository
{
    private readonly AppDbContext _database;

    public EfGoodBehaviourRepository(AppDbContext database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<GoodBehaviourType>> GetActiveTypesAsync(
        CancellationToken cancellationToken)
    {
        return await _database.GoodBehaviourTypes
            .AsNoTracking()
            .Where(type => type.IsActive)
            .OrderBy(type => type.Name)
            .ThenBy(type => type.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<GoodBehaviourType?> GetTypeAsync(Guid typeId, CancellationToken cancellationToken)
    {
        return _database.GoodBehaviourTypes.SingleOrDefaultAsync(
            type => type.Id == typeId,
            cancellationToken);
    }

    public async Task AddTypeAsync(GoodBehaviourType type, CancellationToken cancellationToken)
    {
        await _database.GoodBehaviourTypes.AddAsync(type, cancellationToken);
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

    public async Task<IReadOnlyList<GoodBehaviour>> GetBehavioursByRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        return await _database.GoodBehaviours
            .AsNoTracking()
            .Where(behaviour => behaviour.RequestId == requestId)
            .OrderBy(behaviour => behaviour.ChildId)
            .ToListAsync(cancellationToken);
    }

    public async Task AddBehavioursAsync(
        IReadOnlyCollection<(GoodBehaviour Behaviour, PointsLedgerEntry Award)> entries,
        CancellationToken cancellationToken)
    {
        await _database.GoodBehaviours.AddRangeAsync(
            entries.Select(entry => entry.Behaviour),
            cancellationToken);
        await _database.PointsLedgerEntries.AddRangeAsync(
            entries.Select(entry => entry.Award),
            cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetPointsBalancesAsync(
        IReadOnlyCollection<Guid> childIds,
        CancellationToken cancellationToken)
    {
        return await _database.PointsLedgerEntries
            .AsNoTracking()
            .Where(entry => childIds.Contains(entry.ChildId))
            .GroupBy(entry => entry.ChildId)
            .Select(group => new { ChildId = group.Key, Balance = group.Sum(entry => entry.Amount) })
            .ToDictionaryAsync(item => item.ChildId, item => item.Balance, cancellationToken);
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
                ConstraintName: GoodBehaviourConfiguration.RequestChildIndexName,
            })
        {
            // The failed insert leaves the new entities tracked; detach them so the caller
            // can read the winning request without re-saving them.
            _database.ChangeTracker.Clear();
            throw new DuplicateGoodBehaviourRequestException();
        }
    }
}
