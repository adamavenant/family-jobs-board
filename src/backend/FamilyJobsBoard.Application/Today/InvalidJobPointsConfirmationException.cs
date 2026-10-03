namespace FamilyJobsBoard.Application.Today;

public sealed class InvalidJobPointsConfirmationException : Exception
{
    public InvalidJobPointsConfirmationException(IReadOnlyDictionary<string, string[]> errors)
        : base("The job points confirmation is invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
