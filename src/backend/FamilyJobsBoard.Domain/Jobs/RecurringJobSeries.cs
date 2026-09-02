namespace FamilyJobsBoard.Domain.Jobs;

public sealed class RecurringJobSeries
{
    private RecurringJobSeries()
    {
    }

    private RecurringJobSeries(
        Guid id,
        Guid childId,
        Guid createdByAdultId,
        string name,
        string description,
        int points,
        AgendaPeriod agendaPeriod,
        TimeOnly? scheduledTime,
        DateOnly startDate,
        DateOnly? endDate,
        RecurrenceFrequency frequency,
        int weekdayMask)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A recurring job series needs an ID.", nameof(id));
        }

        if (childId == Guid.Empty)
        {
            throw new ArgumentException("A recurring job series needs an assigned child.", nameof(childId));
        }

        if (createdByAdultId == Guid.Empty)
        {
            throw new ArgumentException("A recurring job series needs a creating adult.", nameof(createdByAdultId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A recurring job series needs a name.", nameof(name));
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > Job.MaximumNameLength)
        {
            throw new ArgumentException(
                $"A job name cannot exceed {Job.MaximumNameLength} characters.",
                nameof(name));
        }

        var trimmedDescription = description.Trim();
        if (trimmedDescription.Length > Job.MaximumDescriptionLength)
        {
            throw new ArgumentException(
                $"A job description cannot exceed {Job.MaximumDescriptionLength} characters.",
                nameof(description));
        }

        if (points < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(points), "Points cannot be negative.");
        }

        if (endDate < startDate)
        {
            throw new ArgumentOutOfRangeException(nameof(endDate), "The end date cannot precede the start date.");
        }

        Id = id;
        ChildId = childId;
        CreatedByAdultId = createdByAdultId;
        Name = trimmedName;
        Description = trimmedDescription;
        Points = points;
        AgendaPeriod = agendaPeriod;
        ScheduledTime = scheduledTime;
        StartDate = startDate;
        EndDate = endDate;
        Frequency = frequency;
        WeekdayMask = weekdayMask;
        GeneratedThrough = startDate.AddDays(-1);
    }

    public Guid Id { get; private set; }

    public Guid ChildId { get; private set; }

    public Guid CreatedByAdultId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public int Points { get; private set; }

    public AgendaPeriod AgendaPeriod { get; private set; }

    public TimeOnly? ScheduledTime { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    public RecurrenceFrequency Frequency { get; private set; }

    public int WeekdayMask { get; private set; }

    public DateOnly GeneratedThrough { get; private set; }

    public static RecurringJobSeries Daily(
        Guid id,
        Guid childId,
        Guid createdByAdultId,
        string name,
        string description,
        int points,
        AgendaPeriod agendaPeriod,
        TimeOnly? scheduledTime,
        DateOnly startDate,
        DateOnly? endDate)
    {
        return new RecurringJobSeries(
            id,
            childId,
            createdByAdultId,
            name,
            description,
            points,
            agendaPeriod,
            scheduledTime,
            startDate,
            endDate,
            RecurrenceFrequency.Daily,
            0);
    }

    public static RecurringJobSeries Weekly(
        Guid id,
        Guid childId,
        Guid createdByAdultId,
        string name,
        string description,
        int points,
        AgendaPeriod agendaPeriod,
        TimeOnly? scheduledTime,
        DateOnly startDate,
        DateOnly? endDate,
        IReadOnlyCollection<DayOfWeek> weekdays)
    {
        ArgumentNullException.ThrowIfNull(weekdays);
        if (weekdays.Count == 0)
        {
            throw new ArgumentException("A weekly job series needs at least one weekday.", nameof(weekdays));
        }

        if (weekdays.Count != weekdays.Distinct().Count())
        {
            throw new ArgumentException("A weekly job series cannot repeat a weekday.", nameof(weekdays));
        }

        var weekdayMask = 0;
        foreach (var weekday in weekdays)
        {
            if (!Enum.IsDefined(weekday))
            {
                throw new ArgumentOutOfRangeException(nameof(weekdays), "Choose valid weekdays.");
            }

            weekdayMask |= 1 << (int)weekday;
        }

        return new RecurringJobSeries(
            id,
            childId,
            createdByAdultId,
            name,
            description,
            points,
            agendaPeriod,
            scheduledTime,
            startDate,
            endDate,
            RecurrenceFrequency.Weekly,
            weekdayMask);
    }

    public IReadOnlyList<DayOfWeek> SelectedWeekdays()
    {
        return Enum.GetValues<DayOfWeek>()
            .Where(Includes)
            .ToArray();
    }

    public DateOnly LastOccurrenceDate(DateOnly horizonInclusive)
    {
        return EndDate is { } endDate && endDate < horizonInclusive
            ? endDate
            : horizonInclusive;
    }

    public IReadOnlyList<DateOnly> GenerateThrough(DateOnly horizonInclusive)
    {
        var lastDate = LastOccurrenceDate(horizonInclusive);
        if (lastDate <= GeneratedThrough)
        {
            return [];
        }

        var firstDate = GeneratedThrough.AddDays(1);
        if (firstDate < StartDate)
        {
            firstDate = StartDate;
        }

        var dates = new List<DateOnly>();
        for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
        {
            if (Frequency == RecurrenceFrequency.Daily || Includes(date.DayOfWeek))
            {
                dates.Add(date);
            }
        }

        GeneratedThrough = lastDate;
        return dates;
    }

    public bool MatchesDaily(
        Guid childId,
        Guid createdByAdultId,
        string name,
        string description,
        int points,
        AgendaPeriod agendaPeriod,
        TimeOnly? scheduledTime,
        DateOnly startDate,
        DateOnly? endDate)
    {
        return Frequency == RecurrenceFrequency.Daily
            && MatchesCommon(
                childId,
                createdByAdultId,
                name,
                description,
                points,
                agendaPeriod,
                scheduledTime,
                startDate,
                endDate);
    }

    public bool MatchesWeekly(
        Guid childId,
        Guid createdByAdultId,
        string name,
        string description,
        int points,
        AgendaPeriod agendaPeriod,
        TimeOnly? scheduledTime,
        DateOnly startDate,
        DateOnly? endDate,
        IReadOnlyCollection<DayOfWeek> weekdays)
    {
        return Frequency == RecurrenceFrequency.Weekly
            && MatchesCommon(
                childId,
                createdByAdultId,
                name,
                description,
                points,
                agendaPeriod,
                scheduledTime,
                startDate,
                endDate)
            && SelectedWeekdays().Order().SequenceEqual(weekdays.Order());
    }

    private bool Includes(DayOfWeek weekday)
    {
        return (WeekdayMask & (1 << (int)weekday)) != 0;
    }

    private bool MatchesCommon(
        Guid childId,
        Guid createdByAdultId,
        string name,
        string description,
        int points,
        AgendaPeriod agendaPeriod,
        TimeOnly? scheduledTime,
        DateOnly startDate,
        DateOnly? endDate)
    {
        return ChildId == childId
            && CreatedByAdultId == createdByAdultId
            && Name == name.Trim()
            && Description == description.Trim()
            && Points == points
            && AgendaPeriod == agendaPeriod
            && ScheduledTime == scheduledTime
            && StartDate == startDate
            && EndDate == endDate;
    }
}
