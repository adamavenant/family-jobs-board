namespace FamilyJobsBoard.Application.Calendar;

public sealed class InvalidCalendarRequestException : Exception
{
    public InvalidCalendarRequestException(IReadOnlyDictionary<string, string[]> errors)
        : base("The calendar request was invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
