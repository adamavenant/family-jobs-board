namespace FamilyJobsBoard.Application.Today;

public sealed class InvalidMonthlyRecurringJobException : Exception
{
    public InvalidMonthlyRecurringJobException(IReadOnlyDictionary<string, string[]> errors)
        : base("The monthly recurring job data was invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
