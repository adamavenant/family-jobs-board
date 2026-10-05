using FamilyJobsBoard.Application.Points;

namespace FamilyJobsBoard.Application.Tests;

/// <summary>Records how a use case held a child's points lock.</summary>
internal sealed class FakeChildPointsLock : IChildPointsLock
{
    public FakeChildPointsLock(Guid childId)
    {
        ChildId = childId;
    }

    public Guid ChildId { get; }

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
