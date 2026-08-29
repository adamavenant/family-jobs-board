namespace FamilyJobsBoard.Application.Clock;

public interface IHouseholdClock
{
    DateOnly Today { get; }

    DateTimeOffset UtcNow { get; }
}
