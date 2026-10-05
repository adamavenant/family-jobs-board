namespace FamilyJobsBoard.Application.Points;

/// <summary>
/// A transaction holding a child's points lock. Every use case that removes points takes it
/// before reading the balance, so two removals can never both pass the no-negative-balance
/// check against the same balance. Disposing without committing rolls the work back.
/// </summary>
public interface IChildPointsLock : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}
