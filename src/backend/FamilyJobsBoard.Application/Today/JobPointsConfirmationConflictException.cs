namespace FamilyJobsBoard.Application.Today;

public sealed class JobPointsConfirmationConflictException : Exception
{
    public JobPointsConfirmationConflictException(Guid jobId)
        : base($"Job '{jobId}' changed after its points award was confirmed. Refresh and try again.")
    {
    }
}
