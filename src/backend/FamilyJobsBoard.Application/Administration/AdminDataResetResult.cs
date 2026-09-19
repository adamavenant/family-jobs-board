namespace FamilyJobsBoard.Application.Administration;

public sealed record AdminDataResetResult(
    Guid ResetId,
    DateTimeOffset OccurredAtUtc,
    int DeletedJobCount,
    int DeletedRecurringSeriesCount,
    int DeletedReviewDecisionCount,
    int DeletedPointsEntryCount);
