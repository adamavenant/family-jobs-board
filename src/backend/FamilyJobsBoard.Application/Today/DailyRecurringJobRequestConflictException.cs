namespace FamilyJobsBoard.Application.Today;

public sealed class DailyRecurringJobRequestConflictException : Exception
{
    public DailyRecurringJobRequestConflictException(Guid requestId)
        : base($"Request '{requestId}' was already used for a different daily recurring job.")
    {
    }
}
