namespace FamilyJobsBoard.Application.Today;

public sealed record TodayBoard(
    TodayMember Viewer,
    IReadOnlyList<TodayMember> Members,
    DateOnly Date,
    IReadOnlyList<TodayJob> Jobs,
    int? PointsBalance,
    IReadOnlyList<TodayPointEarning> PointEarnings,
    int PendingApprovalCount);

public sealed record TodayMember(
    Guid Id,
    string FirstName,
    string? Nickname,
    string DisplayName,
    bool IsAdult);

public sealed record TodayJob(
    Guid Id,
    Guid ChildId,
    string ChildDisplayName,
    string Name,
    string Description,
    int Points,
    string Status,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    TodayJobRejection? LatestRejection);

public sealed record TodayJobRejection(
    Guid DecisionId,
    Guid JobId,
    string? Reason,
    DateTimeOffset RejectedAtUtc);

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
