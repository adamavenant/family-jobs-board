namespace FamilyJobsBoard.Application.Today;

public sealed class RecurringJobChangeForbiddenException : Exception;

public sealed class RecurringJobSeriesNotFoundException : Exception;

public sealed class RecurringJobChangeConflictException : Exception
{
    public RecurringJobChangeConflictException(string message)
        : base(message)
    {
    }

    public RecurringJobChangeConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class RecurringJobChangeConcurrentApplyException : Exception
{
    public RecurringJobChangeConcurrentApplyException(Exception innerException)
        : base("The recurring job change was applied concurrently.", innerException)
    {
    }
}

public sealed class InvalidRecurringJobChangeException : Exception
{
    public InvalidRecurringJobChangeException(IReadOnlyDictionary<string, string[]> errors)
        : base("The recurring job change is invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
