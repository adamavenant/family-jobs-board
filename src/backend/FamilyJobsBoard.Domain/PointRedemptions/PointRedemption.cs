namespace FamilyJobsBoard.Domain.PointRedemptions;

/// <summary>
/// An immutable record of an adult spending a child's points on a reward. A mistaken
/// redemption is corrected with a manual adjustment that adds the points back; existing
/// records are never edited or deleted.
/// </summary>
public sealed class PointRedemption
{
    public const int MaximumRewardLength = 200;

    private PointRedemption()
    {
    }

    public PointRedemption(
        Guid id,
        Guid requestId,
        Guid childId,
        Guid redeemedByMemberId,
        int points,
        string reward,
        DateTimeOffset redeemedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A point redemption needs an ID.", nameof(id));
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("A point redemption needs a request ID.", nameof(requestId));
        }

        if (childId == Guid.Empty)
        {
            throw new ArgumentException("A point redemption needs a child.", nameof(childId));
        }

        if (redeemedByMemberId == Guid.Empty)
        {
            throw new ArgumentException(
                "A point redemption needs the redeeming adult.",
                nameof(redeemedByMemberId));
        }

        if (points < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(points),
                "A point redemption must spend at least one point.");
        }

        Id = id;
        RequestId = requestId;
        ChildId = childId;
        RedeemedByMemberId = redeemedByMemberId;
        Points = points;
        Reward = NormalizeReward(reward);
        RedeemedAtUtc = redeemedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public Guid ChildId { get; private set; }

    public Guid RedeemedByMemberId { get; private set; }

    /// <summary>The number of points spent, always positive.</summary>
    public int Points { get; private set; }

    public string Reward { get; private set; } = string.Empty;

    public DateTimeOffset RedeemedAtUtc { get; private set; }

    public static string NormalizeReward(string? reward)
    {
        var trimmed = reward?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("A point redemption needs a reward.", nameof(reward));
        }

        if (trimmed.Length > MaximumRewardLength)
        {
            throw new ArgumentException(
                $"A point redemption reward cannot exceed {MaximumRewardLength} characters.",
                nameof(reward));
        }

        return trimmed;
    }
}
