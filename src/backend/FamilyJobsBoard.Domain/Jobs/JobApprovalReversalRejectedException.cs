namespace FamilyJobsBoard.Domain.Jobs;

public sealed class JobApprovalReversalRejectedException : InvalidOperationException
{
    public JobApprovalReversalRejectedException(Guid jobId)
        : base($"Job '{jobId}' is not approved, so there is no approval to reverse.")
    {
        JobId = jobId;
    }

    public Guid JobId { get; }
}
