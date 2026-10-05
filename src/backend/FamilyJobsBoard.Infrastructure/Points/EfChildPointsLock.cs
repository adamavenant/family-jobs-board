using FamilyJobsBoard.Application.Points;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FamilyJobsBoard.Infrastructure.Points;

/// <summary>
/// Holds a lock on the child's household member row for the length of a transaction. Every
/// points removal takes it, so removals for one child run one at a time.
/// </summary>
internal sealed class EfChildPointsLock : IChildPointsLock
{
    private readonly IDbContextTransaction _transaction;

    private EfChildPointsLock(IDbContextTransaction transaction)
    {
        _transaction = transaction;
    }

    public static async Task<IChildPointsLock> AcquireAsync(
        AppDbContext database,
        Guid childId,
        CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM household_members WHERE id = {childId} FOR UPDATE",
                cancellationToken);
            return new EfChildPointsLock(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public Task CommitAsync(CancellationToken cancellationToken) =>
        _transaction.CommitAsync(cancellationToken);

    public ValueTask DisposeAsync() => _transaction.DisposeAsync();
}
