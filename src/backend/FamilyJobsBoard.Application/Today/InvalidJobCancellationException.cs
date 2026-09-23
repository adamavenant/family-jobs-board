namespace FamilyJobsBoard.Application.Today;

public sealed class InvalidJobCancellationException : Exception
{
    public InvalidJobCancellationException(IDictionary<string, string[]> errors)
        : base("The job cancellation is invalid.")
    {
        Errors = errors;
    }

    public IDictionary<string, string[]> Errors { get; }
}
