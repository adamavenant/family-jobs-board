using FamilyJobsBoard.Application.TurnRotations;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FamilyJobsBoard.Infrastructure.TurnRotations;

/// <summary>
/// Holds a lock on a rotation's revision rows for the length of a transaction. Ticking a turn
/// off and unticking one both take it, so ticks for one rotation run one at a time.
/// </summary>
internal sealed class EfTurnRotationLock : ITurnRotationLock
{
    private readonly IDbContextTransaction _transaction;

    private EfTurnRotationLock(IDbContextTransaction transaction)
    {
        _transaction = transaction;
    }

    public static async Task<ITurnRotationLock> AcquireAsync(
        AppDbContext database,
        Guid rotationId,
        CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM turn_rotation_revisions WHERE rotation_id = {rotationId} FOR UPDATE",
                cancellationToken);
            return new EfTurnRotationLock(transaction);
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
