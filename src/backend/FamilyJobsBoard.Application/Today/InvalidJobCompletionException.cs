namespace FamilyJobsBoard.Application.Today;

public sealed class InvalidJobCompletionException : Exception
{
    public InvalidJobCompletionException(IReadOnlyDictionary<string, string[]> errors)
        : base("The job completion request is invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
