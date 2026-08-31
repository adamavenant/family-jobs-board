namespace FamilyJobsBoard.Application.Today;

public sealed class InvalidJobRejectionException : Exception
{
    public InvalidJobRejectionException(IReadOnlyDictionary<string, string[]> errors)
        : base("The rejection details were invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
