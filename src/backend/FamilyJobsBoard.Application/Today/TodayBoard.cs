namespace FamilyJobsBoard.Application.Today;

public sealed record TodayBoard(
    Guid ChildId,
    string ChildFirstName,
    string? ChildNickname,
    string ChildDisplayName,
    int PointsBalance,
    DateOnly Date,
    IReadOnlyList<TodayJob> Jobs,
    IReadOnlyList<TodayPointEarning> PointEarnings);

public sealed record TodayJob(
    Guid Id,
    string Name,
    string Description,
    int Points,
    string Status,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? ApprovedAtUtc);

public sealed record TodayJobApproval(TodayJob Job, int PointsBalance);

public sealed record TodayPointsSummary(
    int Balance,
    IReadOnlyList<TodayPointEarning> Earnings);

public sealed record TodayPointEarning(
    Guid Id,
    Guid JobId,
    string JobName,
    int Points,
    DateTimeOffset AwardedAtUtc);
