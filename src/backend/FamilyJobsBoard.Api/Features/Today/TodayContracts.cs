namespace FamilyJobsBoard.Api.Features.Today;

public sealed record TodayResponse(
    ChildResponse Child,
    DateOnly Date,
    IReadOnlyList<JobResponse> Jobs);

public sealed record ChildResponse(Guid Id, string Name, int PointsBalance);

public sealed record AddJobRequest(string? Name, string? Description, int Points);

public sealed record JobResponse(
    Guid Id,
    string Name,
    string Description,
    int Points,
    string Status,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? ApprovedAtUtc);

public sealed record JobApprovalResponse(JobResponse Job, int PointsBalance);
