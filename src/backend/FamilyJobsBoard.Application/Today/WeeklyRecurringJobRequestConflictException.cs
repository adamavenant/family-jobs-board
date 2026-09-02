namespace FamilyJobsBoard.Application.Today;

public sealed class WeeklyRecurringJobRequestConflictException : Exception
{
    public WeeklyRecurringJobRequestConflictException(Guid requestId)
        : base($"Request '{requestId}' was already used for a different recurring job.")
    {
    }
}
