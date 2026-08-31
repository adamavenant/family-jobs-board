namespace FamilyJobsBoard.Application.Today;

public sealed class InvalidTodayJobException : Exception
{
    public InvalidTodayJobException(IDictionary<string, string[]> errors)
        : base("The new job is invalid.")
    {
        Errors = errors;
    }

    public IDictionary<string, string[]> Errors { get; }
}
