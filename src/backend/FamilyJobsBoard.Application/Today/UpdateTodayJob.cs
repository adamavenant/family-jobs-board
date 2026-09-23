namespace FamilyJobsBoard.Application.Today;

public sealed record UpdateTodayJob(
    Guid ViewerId,
    string? Name,
    string? Description,
    int Points,
    DateOnly? ScheduledDate,
    string? AgendaPeriod,
    TimeOnly? ScheduledTime);
