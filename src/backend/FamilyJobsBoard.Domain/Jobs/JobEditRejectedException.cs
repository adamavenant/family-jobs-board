namespace FamilyJobsBoard.Domain.Jobs;

public sealed class JobEditRejectedException : InvalidOperationException
{
    public JobEditRejectedException(Guid jobId)
        : base($"Job '{jobId}' cannot be edited in its current state.")
    {
    }
}
