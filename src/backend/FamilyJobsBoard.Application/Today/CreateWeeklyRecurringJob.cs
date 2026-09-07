namespace FamilyJobsBoard.Application.Today;

public sealed record CreateWeeklyRecurringJob(
    Guid RequestId,
    Guid ViewerId,
    IReadOnlyCollection<Guid>? ChildIds,
    string? Name,
    string? Description,
    int Points,
    string? AgendaPeriod,
    TimeOnly? ScheduledTime,
    DateOnly StartDate,
    DateOnly? EndDate,
    IReadOnlyCollection<string>? Weekdays);
