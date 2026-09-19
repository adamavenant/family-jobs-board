namespace FamilyJobsBoard.Domain.Administration;

public sealed class HouseholdDataReset
{
    private HouseholdDataReset()
    {
    }

    public HouseholdDataReset(
        Guid id,
        Guid initiatedByAdultId,
        DateTimeOffset occurredAtUtc,
        int deletedJobCount,
        int deletedRecurringSeriesCount,
        int deletedReviewDecisionCount,
        int deletedPointsEntryCount)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A data reset needs an ID.", nameof(id));
        }

        if (initiatedByAdultId == Guid.Empty)
        {
            throw new ArgumentException(
                "A data reset needs the initiating adult.",
                nameof(initiatedByAdultId));
        }

        if (deletedJobCount < 0
            || deletedRecurringSeriesCount < 0
            || deletedReviewDecisionCount < 0
            || deletedPointsEntryCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deletedJobCount),
                "Deleted record counts cannot be negative.");
        }

        Id = id;
        InitiatedByAdultId = initiatedByAdultId;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
        DeletedJobCount = deletedJobCount;
        DeletedRecurringSeriesCount = deletedRecurringSeriesCount;
        DeletedReviewDecisionCount = deletedReviewDecisionCount;
        DeletedPointsEntryCount = deletedPointsEntryCount;
    }

    public Guid Id { get; private set; }

    public Guid InitiatedByAdultId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public int DeletedJobCount { get; private set; }

    public int DeletedRecurringSeriesCount { get; private set; }

    public int DeletedReviewDecisionCount { get; private set; }

    public int DeletedPointsEntryCount { get; private set; }
}
