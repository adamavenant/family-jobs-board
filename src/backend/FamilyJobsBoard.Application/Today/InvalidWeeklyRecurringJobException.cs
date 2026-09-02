namespace FamilyJobsBoard.Application.Today;

public sealed class InvalidWeeklyRecurringJobException : Exception
{
    public InvalidWeeklyRecurringJobException(IReadOnlyDictionary<string, string[]> errors)
        : base("The weekly recurring job data was invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
