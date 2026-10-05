using FamilyJobsBoard.Application.PointRedemptions;
using FamilyJobsBoard.Application.Points;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.PointRedemptions;
using FamilyJobsBoard.Domain.Points;
using FamilyJobsBoard.Infrastructure.Data;
using FamilyJobsBoard.Infrastructure.Points;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FamilyJobsBoard.Infrastructure.PointRedemptions;

public sealed class EfPointRedemptionRepository : IPointRedemptionRepository
{
    private readonly AppDbContext _database;

    public EfPointRedemptionRepository(AppDbContext database)
    {
        _database = database;
    }

    public Task<IChildPointsLock> LockChildPointsAsync(
        Guid childId,
        CancellationToken cancellationToken)
    {
        return EfChildPointsLock.AcquireAsync(_database, childId, cancellationToken);
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

    public Task<PointRedemption?> GetRedemptionByRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        return _database.PointRedemptions
            .AsNoTracking()
            .SingleOrDefaultAsync(redemption => redemption.RequestId == requestId, cancellationToken);
    }

    public async Task AddRedemptionAsync(
        PointRedemption redemption,
        PointsLedgerEntry entry,
        CancellationToken cancellationToken)
    {
        await _database.PointRedemptions.AddAsync(redemption, cancellationToken);
        await _database.PointsLedgerEntries.AddAsync(entry, cancellationToken);
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
                ConstraintName: PointRedemptionConfiguration.RequestIdIndexName,
            })
        {
            // Detach the failed inserts so the caller can read the winning request.
            _database.ChangeTracker.Clear();
            throw new DuplicatePointRedemptionRequestException();
        }
    }
}
