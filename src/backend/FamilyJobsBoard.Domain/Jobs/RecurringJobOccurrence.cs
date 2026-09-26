namespace FamilyJobsBoard.Domain.Jobs;

public sealed record RecurringJobOccurrence(DateOnly Date, Guid ChildId);
