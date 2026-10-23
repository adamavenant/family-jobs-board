using FamilyJobsBoard.Application.TurnRotations;

namespace FamilyJobsBoard.Application.Tests;

/// <summary>Records how a use case held a rotation's lock.</summary>
internal sealed class FakeTurnRotationLock : ITurnRotationLock
{
    public FakeTurnRotationLock(Guid rotationId)
    {
        RotationId = rotationId;
    }

    public Guid RotationId { get; }

    public bool SavedWhileHeld { get; set; }

    public bool Committed { get; private set; }

    public bool Disposed { get; private set; }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        if (Disposed)
        {
            throw new InvalidOperationException("The lock was already released.");
        }

        Committed = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
