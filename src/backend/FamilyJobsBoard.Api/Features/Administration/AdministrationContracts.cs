namespace FamilyJobsBoard.Api.Features.Administration;

public sealed record ResetJobsAndPointsRequest(string? Confirmation);

public sealed record ResetJobsAndPointsResponse(
    Guid ResetId,
    DateTimeOffset OccurredAtUtc,
    int DeletedJobCount,
    int DeletedRecurringSeriesCount,
    int DeletedReviewDecisionCount,
    int DeletedPointsEntryCount);
