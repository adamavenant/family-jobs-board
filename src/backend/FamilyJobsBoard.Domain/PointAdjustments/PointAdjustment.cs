namespace FamilyJobsBoard.Domain.PointAdjustments;

/// <summary>
/// An immutable record of an adult manually changing a child's points. A mistaken
/// adjustment is corrected by recording a new, opposite adjustment; existing records
/// are never edited or deleted.
/// </summary>
public sealed class PointAdjustment
{
    public const int MaximumReasonLength = 500;

    private PointAdjustment()
    {
    }

    public PointAdjustment(
        Guid id,
        Guid requestId,
        Guid childId,
        Guid adjustedByMemberId,
        int amount,
        string reason,
        DateTimeOffset adjustedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A point adjustment needs an ID.", nameof(id));
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("A point adjustment needs a request ID.", nameof(requestId));
        }

        if (childId == Guid.Empty)
        {
            throw new ArgumentException("A point adjustment needs a child.", nameof(childId));
        }

        if (adjustedByMemberId == Guid.Empty)
        {
            throw new ArgumentException(
                "A point adjustment needs the adjusting adult.",
                nameof(adjustedByMemberId));
        }

        if (amount == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "A point adjustment must add or remove at least one point.");
        }

        Id = id;
        RequestId = requestId;
        ChildId = childId;
        AdjustedByMemberId = adjustedByMemberId;
        Amount = amount;
        Reason = NormalizeReason(reason);
        AdjustedAtUtc = adjustedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public Guid ChildId { get; private set; }

    public Guid AdjustedByMemberId { get; private set; }

    public int Amount { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public DateTimeOffset AdjustedAtUtc { get; private set; }

    public static string NormalizeReason(string? reason)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("A point adjustment needs a reason.", nameof(reason));
        }

        if (trimmed.Length > MaximumReasonLength)
        {
            throw new ArgumentException(
                $"A point adjustment reason cannot exceed {MaximumReasonLength} characters.",
                nameof(reason));
        }

        return trimmed;
    }
}
