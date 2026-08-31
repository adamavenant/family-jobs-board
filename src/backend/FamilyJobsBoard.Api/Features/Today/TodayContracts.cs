namespace FamilyJobsBoard.Api.Features.Today;

public sealed record TodayResponse(
    ChildResponse Child,
    DateOnly Date,
    IReadOnlyList<JobResponse> Jobs,
    IReadOnlyList<PointEarningResponse> PointEarnings);

public sealed record ChildResponse(
    Guid Id,
    string FirstName,
    string? Nickname,
    string DisplayName,
    int PointsBalance);

public sealed record AddJobRequest(string? Name, string? Description, int Points);

public sealed record RejectJobRequest(string? Reason);

public sealed record JobResponse(
    Guid Id,
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
