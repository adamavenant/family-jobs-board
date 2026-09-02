namespace FamilyJobsBoard.Application.Today;

public sealed record CreateMonthlyRecurringJob(
    Guid RequestId,
    Guid ViewerId,
    Guid ChildId,
    string? Name,
    string? Description,
    int Points,
    string? AgendaPeriod,
    TimeOnly? ScheduledTime,
    DateOnly StartDate,
    DateOnly? EndDate,
    int DayOfMonth);
