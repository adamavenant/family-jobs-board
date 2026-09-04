namespace FamilyJobsBoard.Application.Today;

public sealed class MonthlyRecurringJobRequestConflictException : Exception
{
    public MonthlyRecurringJobRequestConflictException(Guid requestId)
        : base($"Request '{requestId}' was already used for a different recurring job.")
    {
    }
}
