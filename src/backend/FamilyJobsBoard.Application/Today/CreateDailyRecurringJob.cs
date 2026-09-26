namespace FamilyJobsBoard.Application.Today;

public sealed record CreateDailyRecurringJob(
    Guid RequestId,
    Guid ViewerId,
    IReadOnlyCollection<Guid>? ChildIds,
    string? Name,
    string? Description,
    int Points,
    string? AgendaPeriod,
    TimeOnly? ScheduledTime,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? AssignmentMode = null);

public sealed record RecurringJobAssignment(
    Guid SeriesId,
    Guid ChildId,
    DateOnly GeneratedThrough,
    int OccurrenceCount,
    IReadOnlyList<Guid> RotationChildIds);

public sealed record RecurringJobCreation(
    IReadOnlyList<RecurringJobAssignment> Assignments,
    bool WasCreated);
