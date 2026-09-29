namespace FamilyJobsBoard.Application.Today;

public sealed class JobStateConflictException : Exception
{
    public JobStateConflictException(Exception innerException)
        : base("The job changed while this action was being completed. Refresh and try again.", innerException)
    {
    }
}
