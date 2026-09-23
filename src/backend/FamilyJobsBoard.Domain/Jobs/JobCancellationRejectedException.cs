namespace FamilyJobsBoard.Domain.Jobs;

public sealed class JobCancellationRejectedException : InvalidOperationException
{
    public JobCancellationRejectedException(Guid jobId)
        : base($"Job '{jobId}' cannot be cancelled in its current state.")
    {
    }
}
