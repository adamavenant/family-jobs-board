namespace FamilyJobsBoard.Api.Features.Today;

public sealed record TodayResponse(
    ChildResponse Child,
    DateOnly Date,
    IReadOnlyList<JobResponse> Jobs);

public sealed record ChildResponse(Guid Id, string Name);

public sealed record JobResponse(
    Guid Id,
    string Name,
    string Description,
    int Points,
    string Status,
    DateTimeOffset? CompletedAtUtc);
