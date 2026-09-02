namespace FamilyJobsBoard.Application.Today;

public sealed record CreateDailyRecurringJob(
    Guid RequestId,
    Guid ViewerId,
    Guid ChildId,
    string? Name,
    string? Description,
    int Points,
    string? AgendaPeriod,
    TimeOnly? ScheduledTime,
    DateOnly StartDate,
    DateOnly? EndDate);

public sealed record DailyRecurringJobCreation(
    Guid SeriesId,
    DateOnly GeneratedThrough,
    int OccurrenceCount,
    bool WasCreated);
