namespace FamilyJobsBoard.Application.Today;

public sealed class InvalidJobEditException : Exception
{
    public InvalidJobEditException(IDictionary<string, string[]> errors)
        : base("The edited job is invalid.")
    {
        Errors = errors;
    }

    public IDictionary<string, string[]> Errors { get; }
}
