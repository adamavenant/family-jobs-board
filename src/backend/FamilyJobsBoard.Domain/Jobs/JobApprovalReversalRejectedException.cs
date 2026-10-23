namespace FamilyJobsBoard.Domain.Jobs;

public sealed class JobApprovalReversalRejectedException : InvalidOperationException
{
    public JobApprovalReversalRejectedException(Guid jobId)
        : base("This job isn't approved any more, so there's no approval to reverse. Refresh to see its latest state.")
    {
        JobId = jobId;
    }

    public Guid JobId { get; }
}
