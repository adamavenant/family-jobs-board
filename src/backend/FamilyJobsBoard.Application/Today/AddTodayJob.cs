namespace FamilyJobsBoard.Application.Today;

public sealed record AddTodayJob(
    IReadOnlyCollection<Guid>? ChildIds,
    string? Name,
    string? Description,
    int Points);
