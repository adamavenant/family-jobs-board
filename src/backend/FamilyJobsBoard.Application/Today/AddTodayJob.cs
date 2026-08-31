namespace FamilyJobsBoard.Application.Today;

public sealed record AddTodayJob(
    Guid ChildId,
    string? Name,
    string? Description,
    int Points);
