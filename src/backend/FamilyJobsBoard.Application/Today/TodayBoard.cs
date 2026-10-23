using FamilyJobsBoard.Application.TurnRotations;

namespace FamilyJobsBoard.Application.Today;

public sealed record TodayBoard(
    TodayMember Viewer,
    IReadOnlyList<TodayMember> Members,
    DateOnly Date,
    DateOnly CurrentDate,
    Guid? SelectedChildId,
    IReadOnlyList<TodayJob> Jobs,
    int? PointsBalance,
    int PendingApprovalCount,
    IReadOnlyList<TodayWhoseTurn> WhoseTurns,
    int UndoGracePeriodSeconds);

public sealed record TodayWhoseTurn(
    Guid RotationId,
    string Question,
    Guid ChildId,
    string ChildDisplayName,
    Guid RevisionId,
    int TurnNumber,
    string AdvanceMode,
    bool TurnTaken,
    bool CanTakeTurn,
    TurnRotationTakenTurn? UndoableTurn);

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
    DateOnly ScheduledDate,
    string AgendaPeriod,
    TimeOnly? ScheduledTime,
    Guid? RecurringJobSeriesId,
    string? RecurrenceFrequency,
    string Status,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    TodayJobRejection? LatestRejection,
    TodayRecurringJobSeries? Recurrence);

public sealed record TodayRecurringJobSeries(
    Guid SeriesId,
    int Version,
    string Frequency,
    IReadOnlyList<string> Weekdays,
    int? DayOfMonth,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool TakesTurns);

public sealed record RecurringJobSlot(
    Guid SeriesId,
    DateOnly ScheduledDate,
    DateOnly OriginalScheduledDate);

public sealed record TodayJobRejection(
    Guid DecisionId,
    Guid JobId,
    string? Reason,
    DateTimeOffset RejectedAtUtc);

public sealed record TodayJobApproval(TodayJob Job, int PointsBalance);

public sealed record TodayJobApprovalReversal(TodayJob Job, int PointsBalance);
