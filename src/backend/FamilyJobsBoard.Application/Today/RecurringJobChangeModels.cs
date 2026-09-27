namespace FamilyJobsBoard.Application.Today;

public sealed record RecurringJobChangeInput(
    string? Operation,
    string? Scope,
    string? Reason,
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

public sealed record RecurringJobChangeImpact(
    int UpdatedCount,
    int CreatedCount,
    int CancelledCount,
    int ApprovedSkippedCount,
    int CancelledSkippedCount,
    int RetrospectivePointIncreaseSkippedCount,
    IReadOnlyList<string> Warnings);

public sealed record RecurringJobScopePreview(
    RecurringJobChangeImpact? Impact,
    string? Error);

public sealed record RecurringJobChangePreview(
    int SeriesVersion,
    RecurringJobScopePreview ThisOnly,
    RecurringJobScopePreview AllFuture,
    RecurringJobScopePreview All);

public sealed record RecurringJobChangeResult(
    Guid RequestId,
    Guid SeriesId,
    int SeriesVersion,
    string Operation,
    string Scope,
    RecurringJobChangeImpact Impact);
