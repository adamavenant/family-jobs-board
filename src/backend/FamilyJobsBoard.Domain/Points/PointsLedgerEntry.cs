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
        : this(id, childId, EnsureAward(amount), awardedAtUtc)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("A points award needs a source job.", nameof(jobId));
        }

        JobId = jobId;
    }

    private PointsLedgerEntry(
        Guid id,
        Guid childId,
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

        Id = id;
        ChildId = childId;
        Amount = amount;
        AwardedAtUtc = awardedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }

    public Guid ChildId { get; private set; }

    public Guid? JobId { get; private set; }

    public Guid? GoodBehaviourId { get; private set; }

    public Guid? PointAdjustmentId { get; private set; }

    public int Amount { get; private set; }

    public DateTimeOffset AwardedAtUtc { get; private set; }

    public static PointsLedgerEntry ForGoodBehaviour(
        Guid id,
        Guid childId,
        Guid goodBehaviourId,
        int amount,
        DateTimeOffset awardedAtUtc)
    {
        if (goodBehaviourId == Guid.Empty)
        {
            throw new ArgumentException(
                "A points award needs a source good behaviour.",
                nameof(goodBehaviourId));
        }

        return new PointsLedgerEntry(id, childId, EnsureAward(amount), awardedAtUtc)
        {
            GoodBehaviourId = goodBehaviourId,
        };
    }

    /// <summary>
    /// The only kind of entry that may be negative: a manual adjustment by an adult.
    /// </summary>
    public static PointsLedgerEntry ForManualAdjustment(
        Guid id,
        Guid childId,
        Guid pointAdjustmentId,
        int amount,
        DateTimeOffset awardedAtUtc)
    {
        if (pointAdjustmentId == Guid.Empty)
        {
            throw new ArgumentException(
                "A points adjustment needs a source point adjustment.",
                nameof(pointAdjustmentId));
        }

        if (amount == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "A points adjustment cannot be zero.");
        }

        return new PointsLedgerEntry(id, childId, amount, awardedAtUtc)
        {
            PointAdjustmentId = pointAdjustmentId,
        };
    }

    private static int EnsureAward(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Awarded points cannot be negative.");
        }

        return amount;
    }
}
