using FamilyJobsBoard.Application.PointAdjustments;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.PointAdjustments;
using FamilyJobsBoard.Domain.Points;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FamilyJobsBoard.Infrastructure.PointAdjustments;

public sealed class EfPointAdjustmentRepository : IPointAdjustmentRepository
{
    private readonly AppDbContext _database;

    public EfPointAdjustmentRepository(AppDbContext database)
    {
        _database = database;
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

    public Task<PointAdjustment?> GetAdjustmentByRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        return _database.PointAdjustments
            .AsNoTracking()
            .SingleOrDefaultAsync(adjustment => adjustment.RequestId == requestId, cancellationToken);
    }

    public async Task AddAdjustmentAsync(
        PointAdjustment adjustment,
        PointsLedgerEntry entry,
        CancellationToken cancellationToken)
    {
        await _database.PointAdjustments.AddAsync(adjustment, cancellationToken);
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
                ConstraintName: PointAdjustmentConfiguration.RequestIdIndexName,
            })
        {
            // Detach the failed inserts so the caller can read the winning request.
            _database.ChangeTracker.Clear();
            throw new DuplicatePointAdjustmentRequestException();
        }
    }
}
