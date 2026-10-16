namespace FamilyJobsBoard.Application.TurnRotations;

/// <summary>
/// A transaction holding a rotation's lock. Ticking a turn off and unticking one both take it
/// before reading the rotation's ticks, so two ticks can never both pass the "whose turn is
/// it" check against the same state. Disposing without committing rolls the work back.
/// </summary>
public interface ITurnRotationLock : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}
