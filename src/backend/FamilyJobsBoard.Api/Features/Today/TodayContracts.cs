namespace FamilyJobsBoard.Api.Features.Today;

public sealed record TodayResponse(
    MemberResponse Viewer,
    IReadOnlyList<MemberResponse> Members,
    DateOnly Date,
    DateOnly CurrentDate,
    Guid? SelectedChildId,
    IReadOnlyList<JobResponse> Jobs,
    int? PointsBalance,
    IReadOnlyList<PointEarningResponse> PointEarnings,
    int PendingApprovalCount,
    IReadOnlyList<WhoseTurnResponse> WhoseTurns);

public sealed record WhoseTurnResponse(
    Guid RotationId,
    string Question,
    Guid ChildId,
    string ChildDisplayName);

public sealed record MemberResponse(
    Guid Id,
    string FirstName,
    string? Nickname,
    string DisplayName,
    bool IsAdult);

public sealed record AddJobRequest(
    IReadOnlyList<Guid>? ChildIds,
    string? Name,
    string? Description,
    int Points,
    DateOnly? ScheduledDate,
    string? AgendaPeriod,
    TimeOnly? ScheduledTime);

public sealed record CreateDailyRecurringJobRequest(
    Guid RequestId,
    IReadOnlyList<Guid>? ChildIds,
    string? Name,
    string? Description,
    int Points,
    string? AgendaPeriod,
    TimeOnly? ScheduledTime,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? AssignmentMode = null);

public sealed record CreateWeeklyRecurringJobRequest(
    Guid RequestId,
    IReadOnlyList<Guid>? ChildIds,
    string? Name,
    string? Description,
    int Points,
    string? AgendaPeriod,
    TimeOnly? ScheduledTime,
    DateOnly StartDate,
    DateOnly? EndDate,
    IReadOnlyList<string>? Weekdays,
    string? AssignmentMode = null);

public sealed record CreateMonthlyRecurringJobRequest(
    Guid RequestId,
    IReadOnlyList<Guid>? ChildIds,
    string? Name,
    string? Description,
    int Points,
    string? AgendaPeriod,
    TimeOnly? ScheduledTime,
    DateOnly StartDate,
    DateOnly? EndDate,
    int DayOfMonth,
    string? AssignmentMode = null);

public sealed record RecurringJobAssignmentResponse(
    Guid SeriesId,
    Guid ChildId,
    DateOnly GeneratedThrough,
    int OccurrenceCount,
    IReadOnlyList<Guid> RotationChildIds);

public sealed record RecurringJobResponse(
    IReadOnlyList<RecurringJobAssignmentResponse> Assignments);

public sealed record AddJobsResponse(IReadOnlyList<JobResponse> Jobs);

public sealed record RejectJobRequest(string? Reason);

public sealed record UpdateJobRequest(
    string? Name,
    string? Description,
    int Points,
    DateOnly? ScheduledDate,
    string? AgendaPeriod,
    TimeOnly? ScheduledTime);

public sealed record CancelJobRequest(string? Reason);

public sealed record JobResponse(
    Guid Id,
    Guid ChildId,
    string ChildDisplayName,
    string Name,
    string Description,
    int Points,
    DateOnly ScheduledDate,
    string AgendaPeriod,
    TimeOnly? ScheduledTime,
    Guid? RecurringJobSeriesId,
    string? RecurrenceFrequency,
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
    string Source,
    string Name,
    Guid? JobId,
    int Points,
    DateTimeOffset AwardedAtUtc,
    string? LoggedByDisplayName);
