namespace FamilyJobsBoard.Application.Today;

public sealed record RecurringJobChangeInput(
    string? Operation,
    string? Scope,
    int ExpectedSeriesVersion,
    string? Name,
    string? Description,
    int Points,
    DateOnly? ScheduledDate,
    string? AgendaPeriod,
    TimeOnly? ScheduledTime,
    string? Frequency,
    IReadOnlyList<string>? Weekdays,
    int? DayOfMonth,
    DateOnly? EndDate);

public sealed record ApplyRecurringJobChange(
    Guid RequestId,
    RecurringJobChangeInput Change);

public sealed record RecurringJobSeriesDetails(
    Guid SeriesId,
    int Version,
    string Frequency,
    IReadOnlyList<string> Weekdays,
    int? DayOfMonth,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool TakesTurns);

public sealed record RecurringJobChangeImpact(
    int UpdatedCount,
    int CreatedCount,
    int CancelledCount,
    int ApprovedSkippedCount,
    int CancelledSkippedCount,
    int RetrospectivePointIncreaseSkippedCount,
    IReadOnlyList<string> Warnings);

public sealed record RecurringJobChangeResult(
    Guid RequestId,
    Guid SeriesId,
    int SeriesVersion,
    string Operation,
    string Scope,
    RecurringJobChangeImpact Impact);
