namespace FamilyJobsBoard.Domain.Jobs;

public sealed class JobCompletionRejectedException : InvalidOperationException
{
    public JobCompletionRejectedException(Guid jobId)
        : base($"Job '{jobId}' is not open and cannot be completed again.")
    {
        JobId = jobId;
    }

    public Guid JobId { get; }
}
