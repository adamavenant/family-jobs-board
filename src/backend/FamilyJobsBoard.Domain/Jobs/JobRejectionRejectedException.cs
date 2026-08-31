namespace FamilyJobsBoard.Domain.Jobs;

public sealed class JobRejectionRejectedException : InvalidOperationException
{
    public JobRejectionRejectedException(Guid jobId)
        : base($"Job '{jobId}' is not pending approval and cannot be rejected.")
    {
    }
}
