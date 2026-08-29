namespace FamilyJobsBoard.Application.Today;

public sealed record TodayBoard(
    Guid ChildId,
    string ChildName,
    DateOnly Date,
    IReadOnlyList<TodayJob> Jobs);

public sealed record TodayJob(
    Guid Id,
    string Name,
    string Description,
    int Points,
    string Status,
    DateTimeOffset? CompletedAtUtc);
