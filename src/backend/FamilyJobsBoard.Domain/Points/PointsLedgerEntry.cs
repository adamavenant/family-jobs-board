namespace FamilyJobsBoard.Domain.Points;

public sealed class PointsLedgerEntry
{
    private PointsLedgerEntry()
    {
    }

    public PointsLedgerEntry(
        Guid id,
        Guid childId,
        Guid jobId,
        int amount,
        DateTimeOffset awardedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A points award needs an ID.", nameof(id));
        }

        if (childId == Guid.Empty)
        {
            throw new ArgumentException("A points award needs a child.", nameof(childId));
        }

        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("A points award needs a source job.", nameof(jobId));
        }

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Awarded points cannot be negative.");
        }

        Id = id;
        ChildId = childId;
        JobId = jobId;
        Amount = amount;
        AwardedAtUtc = awardedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }

    public Guid ChildId { get; private set; }

    public Guid JobId { get; private set; }

    public int Amount { get; private set; }

    public DateTimeOffset AwardedAtUtc { get; private set; }
}
