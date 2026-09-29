namespace FamilyJobsBoard.Application.Today;

public sealed class JobCompletionConfirmationConflictException : Exception
{
    public JobCompletionConfirmationConflictException(Guid jobId)
        : base($"Job '{jobId}' changed after its points award was confirmed. Refresh and try again.")
    {
    }
}
