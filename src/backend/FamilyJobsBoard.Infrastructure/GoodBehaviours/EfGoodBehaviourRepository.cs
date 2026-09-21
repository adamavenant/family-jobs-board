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

    public Task<HouseholdMember?> GetActiveChildAsync(
        Guid childId,
        CancellationToken cancellationToken)
    {
        return _database.HouseholdMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                member => member.Id == childId
                    && member.IsActive
                    && member.Role == HouseholdRole.Child,
                cancellationToken);
    }

    public Task<GoodBehaviour?> GetBehaviourByRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        return _database.GoodBehaviours
            .AsNoTracking()
            .SingleOrDefaultAsync(behaviour => behaviour.RequestId == requestId, cancellationToken);
    }

    public async Task AddBehaviourAsync(
        GoodBehaviour behaviour,
        PointsLedgerEntry award,
        CancellationToken cancellationToken)
    {
        await _database.GoodBehaviours.AddAsync(behaviour, cancellationToken);
        await _database.PointsLedgerEntries.AddAsync(award, cancellationToken);
    }

    public Task<int> GetPointsBalanceAsync(Guid childId, CancellationToken cancellationToken)
    {
        return _database.PointsLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.ChildId == childId)
            .SumAsync(entry => entry.Amount, cancellationToken);
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
                ConstraintName: GoodBehaviourConfiguration.RequestIdIndexName,
            })
        {
            // The failed insert leaves the new entities tracked; detach them so the caller
            // can read the winning request without re-saving them.
            _database.ChangeTracker.Clear();
            throw new DuplicateGoodBehaviourRequestException();
        }
    }
}
