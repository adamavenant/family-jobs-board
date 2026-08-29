namespace FamilyJobsBoard.Application.Today;

public sealed class JobNotFoundException : InvalidOperationException
{
    public JobNotFoundException(Guid jobId)
        : base($"Job '{jobId}' was not found.")
    {
        JobId = jobId;
    }

    public Guid JobId { get; }
}
