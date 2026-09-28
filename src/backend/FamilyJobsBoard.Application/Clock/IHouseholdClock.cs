namespace FamilyJobsBoard.Application.Clock;

public interface IHouseholdClock
{
    DateOnly Today { get; }

    DateTimeOffset UtcNow { get; }

    DateTimeOffset ToUtc(DateOnly date, TimeOnly time) =>
        new(date.ToDateTime(time, DateTimeKind.Unspecified), TimeSpan.Zero);
}
