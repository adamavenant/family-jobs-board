namespace FamilyJobsBoard.Application.Today;

public sealed class InvalidDailyRecurringJobException : Exception
{
    public InvalidDailyRecurringJobException(IDictionary<string, string[]> errors)
        : base("The daily recurring job is invalid.")
    {
        Errors = errors;
    }

    public IDictionary<string, string[]> Errors { get; }
}
