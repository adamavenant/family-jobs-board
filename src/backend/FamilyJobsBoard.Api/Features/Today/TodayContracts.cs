namespace FamilyJobsBoard.Api.Features.Today;

public sealed record TodayResponse(
    MemberResponse Viewer,
    IReadOnlyList<MemberResponse> Members,
    DateOnly Date,
    IReadOnlyList<JobResponse> Jobs,
    int? PointsBalance,
    IReadOnlyList<PointEarningResponse> PointEarnings,
    int PendingApprovalCount);

public sealed record MemberResponse(
    Guid Id,
    string FirstName,
    string? Nickname,
    string DisplayName,
    bool IsAdult);

public sealed record AddJobRequest(
    Guid ChildId,
    string? Name,
    string? Description,
    int Points);

public sealed record RejectJobRequest(string? Reason);

public sealed record JobResponse(
    Guid Id,
    Guid ChildId,
    string ChildDisplayName,
    string Name,
    string Description,
    int Points,
    string Status,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    JobRejectionResponse? LatestRejection);

public sealed record JobRejectionResponse(
    Guid DecisionId,
    string? Reason,
    DateTimeOffset RejectedAtUtc);

public sealed record JobApprovalResponse(JobResponse Job, int PointsBalance);

public sealed record PointEarningResponse(
    Guid Id,
    Guid JobId,
    string JobName,
    int Points,
    DateTimeOffset AwardedAtUtc);
