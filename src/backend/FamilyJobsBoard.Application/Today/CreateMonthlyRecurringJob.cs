namespace FamilyJobsBoard.Application.Today;

public sealed record CreateMonthlyRecurringJob(
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
    int DayOfMonth,
    string? AssignmentMode = null);
