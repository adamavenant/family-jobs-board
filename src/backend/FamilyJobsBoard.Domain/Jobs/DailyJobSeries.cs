namespace FamilyJobsBoard.Domain.Jobs;

public sealed class DailyJobSeries
{
    private DailyJobSeries()
    {
    }

    public DailyJobSeries(
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
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A daily job series needs an ID.", nameof(id));
        }

        if (childId == Guid.Empty)
        {
            throw new ArgumentException("A daily job series needs an assigned child.", nameof(childId));
        }

        if (createdByAdultId == Guid.Empty)
        {
            throw new ArgumentException("A daily job series needs a creating adult.", nameof(createdByAdultId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A daily job series needs a name.", nameof(name));
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

    public DateOnly GeneratedThrough { get; private set; }

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
            dates.Add(date);
        }

        GeneratedThrough = lastDate;
        return dates;
    }

    public bool Matches(
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
