namespace FamilyJobsBoard.Application.Today;

public sealed record TodayBoard(
    Guid ChildId,
    string ChildName,
    int PointsBalance,
    DateOnly Date,
    IReadOnlyList<TodayJob> Jobs);

public sealed record TodayJob(
    Guid Id,
    string Name,
    string Description,
    int Points,
    string Status,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? ApprovedAtUtc);

public sealed record TodayJobApproval(TodayJob Job, int PointsBalance);
