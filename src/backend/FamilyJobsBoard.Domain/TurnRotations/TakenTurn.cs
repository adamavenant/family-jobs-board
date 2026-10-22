namespace FamilyJobsBoard.Domain.TurnRotations;

/// <summary>How a rotation moves on to the next child.</summary>
public enum TurnAdvanceMode
{
    /// <summary>Every household-local calendar day moves the rotation on.</summary>
    Daily,

    /// <summary>The rotation moves on only when the current child's turn is ticked off.</summary>
    WhenTicked,
}

/// <summary>
/// A tick recording that a child took their turn in a rotation that moves on when ticked.
/// Ticks are append-only: unticking marks one undone with who and when, and it then no
/// longer counts towards the rotation.
/// </summary>
public sealed class TakenTurn
{
    private TakenTurn()
    {
    }

    internal TakenTurn(
        Guid id,
        Guid requestId,
        Guid rotationId,
        Guid revisionId,
        int turnNumber,
        DateOnly date,
        Guid childId,
        Guid recordedByMemberId,
        DateTimeOffset recordedAtUtc)
    {
        if (id == Guid.Empty || requestId == Guid.Empty || rotationId == Guid.Empty || revisionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A taken turn needs an ID, a request ID, a rotation ID, and a revision ID.",
                nameof(id));
        }

        if (turnNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(turnNumber), "A turn number cannot be negative.");
        }

        if (childId == Guid.Empty || recordedByMemberId == Guid.Empty)
        {
            throw new ArgumentException("A taken turn needs a child and the member who ticked it.", nameof(childId));
        }

        Id = id;
        RequestId = requestId;
        RotationId = rotationId;
        RevisionId = revisionId;
        TurnNumber = turnNumber;
        Date = date;
        ChildId = childId;
        RecordedByMemberId = recordedByMemberId;
        RecordedAtUtc = recordedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }

    /// <summary>Identifies the request that ticked the turn, so a replay never ticks again.</summary>
    public Guid RequestId { get; private set; }

    public Guid RotationId { get; private set; }

    /// <summary>The revision the tick counts towards; a later revision starts afresh.</summary>
    public Guid RevisionId { get; private set; }

    /// <summary>Which of the revision's turns this tick took, counting from zero.</summary>
    public int TurnNumber { get; private set; }

    /// <summary>The household-local date the turn was taken.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The child whose turn was taken.</summary>
    public Guid ChildId { get; private set; }

    public Guid RecordedByMemberId { get; private set; }

    public DateTimeOffset RecordedAtUtc { get; private set; }

    public Guid? UndoneByMemberId { get; private set; }

    public DateTimeOffset? UndoneAtUtc { get; private set; }

    public bool IsUndone => UndoneAtUtc is not null;

    public void Undo(Guid undoneByMemberId, DateTimeOffset undoneAtUtc)
    {
        if (undoneByMemberId == Guid.Empty)
        {
            throw new ArgumentException("Unticking a turn needs the member who did it.", nameof(undoneByMemberId));
        }

        if (IsUndone)
        {
            throw new InvalidOperationException("This turn has already been unticked.");
        }

        UndoneByMemberId = undoneByMemberId;
        UndoneAtUtc = undoneAtUtc.ToUniversalTime();
    }
}

/// <summary>
/// Whose turn it is on a date, whether that turn has been ticked off, and its number within
/// the revision (counting from zero): days since the effective date in a daily rotation, or
/// turns ticked off before it in a rotation that moves on when ticked. A revision and a turn
/// number together identify one turn, even when the same child's turn comes round again.
/// </summary>
public readonly record struct TurnRotationTurn(Guid ChildId, bool IsTaken, int Number);
