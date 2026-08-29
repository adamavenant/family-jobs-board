using FamilyJobsBoard.Application.Clock;

namespace FamilyJobsBoard.Infrastructure.Time;

public sealed class SystemHouseholdClock : IHouseholdClock
{
    private readonly TimeZoneInfo _timeZone;

    public SystemHouseholdClock(string timeZoneId)
    {
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly Today
    {
        get
        {
            var householdNow = TimeZoneInfo.ConvertTime(UtcNow, _timeZone);
            return DateOnly.FromDateTime(householdNow.DateTime);
        }
    }
}
