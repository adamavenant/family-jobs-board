namespace FamilyJobsBoard.Domain.Jobs;

public sealed class RecurringJobSeriesRevision
{
    private RecurringJobSeriesRevision()
    {
    }

    public RecurringJobSeriesRevision(
        Guid id,
        Guid seriesId,
        Guid changeRequestId,
        Guid createdByAdultId,
        DateTimeOffset createdAtUtc,
        DateOnly effectiveFrom,
        string name,
        string description,
        int points,
        AgendaPeriod agendaPeriod,
        TimeOnly? scheduledTime,
        DateOnly? endDate,
        RecurrenceFrequency frequency,
        int weekdayMask,
        int? monthlyDay,
        bool ended)
    {
        Id = id;
        SeriesId = seriesId;
        ChangeRequestId = changeRequestId;
        CreatedByAdultId = createdByAdultId;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        EffectiveFrom = effectiveFrom;
        Name = name;
        Description = description;
        Points = points;
        AgendaPeriod = agendaPeriod;
        ScheduledTime = scheduledTime;
        EndDate = endDate;
        Frequency = frequency;
        WeekdayMask = weekdayMask;
        MonthlyDay = monthlyDay;
        Ended = ended;
    }

    public Guid Id { get; private set; }
    public Guid SeriesId { get; private set; }
    public Guid ChangeRequestId { get; private set; }
    public Guid CreatedByAdultId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public int Points { get; private set; }
    public AgendaPeriod AgendaPeriod { get; private set; }
    public TimeOnly? ScheduledTime { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public RecurrenceFrequency Frequency { get; private set; }
    public int WeekdayMask { get; private set; }
    public int? MonthlyDay { get; private set; }
    public bool Ended { get; private set; }
}
