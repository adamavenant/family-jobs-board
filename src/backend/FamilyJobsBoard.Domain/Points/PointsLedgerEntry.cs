using FamilyJobsBoard.Domain.Jobs;

namespace FamilyJobsBoard.Domain.Points;

public sealed class PointsLedgerEntry
{
    private PointsLedgerEntry()
    {
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

    /// <summary>
    /// The job's name when the entry was written, so history keeps it if the job is renamed
    /// after a reversal; null for entries that aren't from a job.
    /// </summary>
    public string? JobName { get; private set; }

    /// <summary>Which approval of the job a job award is for; null for every other entry.</summary>
    public int? JobApprovalNumber { get; private set; }

    /// <summary>The job award an approval reversal takes back; null for every other entry.</summary>
    public Guid? ReversedEntryId { get; private set; }

    public Guid? GoodBehaviourId { get; private set; }

    public Guid? PointAdjustmentId { get; private set; }

    public Guid? PointRedemptionId { get; private set; }

    public int Amount { get; private set; }

    public DateTimeOffset AwardedAtUtc { get; private set; }

    public bool IsApprovalReversal => ReversedEntryId is not null;

    /// <summary>
    /// The points award for a job's latest approval, numbered by it. A job is only approved
    /// again after its previous approval was reversed, so each approval awards once and a job
    /// has at most one award that hasn't been reversed.
    /// </summary>
    public static PointsLedgerEntry ForJobApproval(
        Guid id,
        Job job,
        DateTimeOffset awardedAtUtc)
    {
        if (job.Status != JobStatus.Approved)
        {
            throw new ArgumentException("Only an approved job awards points.", nameof(job));
        }

        return new PointsLedgerEntry(id, job.ChildId, EnsureAward(job.Points), awardedAtUtc)
        {
            JobId = job.Id,
            JobName = job.Name,
            JobApprovalNumber = job.ApprovalCount,
        };
    }

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
    /// A manual adjustment by an adult: the only kind of entry that may be either sign.
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

    /// <summary>
    /// A reward redemption: always negative, spending <paramref name="points"/> points.
    /// </summary>
    public static PointsLedgerEntry ForRedemption(
        Guid id,
        Guid childId,
        Guid pointRedemptionId,
        int points,
        DateTimeOffset redeemedAtUtc)
    {
        if (pointRedemptionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A points redemption needs a source point redemption.",
                nameof(pointRedemptionId));
        }

        if (points < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(points),
                "A points redemption must spend at least one point.");
        }

        return new PointsLedgerEntry(id, childId, -points, redeemedAtUtc)
        {
            PointRedemptionId = pointRedemptionId,
        };
    }

    /// <summary>
    /// Takes back a job award whose approval was reversed: a new entry for the same job and
    /// child with the opposite amount. The award itself is never changed.
    /// </summary>
    public static PointsLedgerEntry ForApprovalReversal(
        Guid id,
        PointsLedgerEntry award,
        DateTimeOffset reversedAtUtc)
    {
        if (award.JobId is null || award.IsApprovalReversal)
        {
            throw new ArgumentException("Only a job award can be reversed.", nameof(award));
        }

        return new PointsLedgerEntry(id, award.ChildId, -award.Amount, reversedAtUtc)
        {
            JobId = award.JobId,
            JobName = award.JobName,
            ReversedEntryId = award.Id,
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
