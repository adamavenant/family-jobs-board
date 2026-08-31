namespace FamilyJobsBoard.Domain.Jobs;

public sealed class JobApprovalRejectedException : InvalidOperationException
{
    public JobApprovalRejectedException(Guid jobId)
        : base($"Job '{jobId}' is not pending approval and cannot be approved.")
    {
        JobId = jobId;
    }

    public Guid JobId { get; }
}
