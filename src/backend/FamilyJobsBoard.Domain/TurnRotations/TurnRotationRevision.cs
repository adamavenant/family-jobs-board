namespace FamilyJobsBoard.Domain.TurnRotations;

/// <summary>
/// An immutable, effective-dated configuration of one of the household's "whose turn"
/// rotations. A new revision is created whenever an adult reconfigures the rotation;
/// existing revisions are never edited, so historical answers stay stable.
/// </summary>
public sealed class TurnRotationRevision
{
    public const int MaximumQuestionLength = 200;
    public const string DefaultQuestion = "Who is Pink today?";

    private readonly List<TurnRotationParticipant> _participants = [];

    private TurnRotationRevision()
    {
    }

    public TurnRotationRevision(
        Guid id,
        Guid rotationId,
        DateOnly effectiveFrom,
        string? question,
        IReadOnlyList<Guid> participantChildIds,
        Guid firstChildId,
        Guid createdByMemberId,
        DateTimeOffset createdAtUtc,
        TurnAdvanceMode advanceMode = TurnAdvanceMode.Daily)
    {
        if (id == Guid.Empty || rotationId == Guid.Empty)
        {
            throw new ArgumentException("A turn rotation revision needs an ID and a rotation ID.", nameof(id));
        }

        if (createdByMemberId == Guid.Empty)
        {
            throw new ArgumentException(
                "A turn rotation revision needs a creator ID.",
                nameof(createdByMemberId));
        }

        var childIds = (participantChildIds ?? []).ToArray();
        if (childIds.Length == 0)
        {
            throw new ArgumentException(
                "A turn rotation needs at least one participant.",
                nameof(participantChildIds));
        }

        if (childIds.Any(childId => childId == Guid.Empty))
        {
            throw new ArgumentException(
                "A turn rotation participant needs a child ID.",
                nameof(participantChildIds));
        }

        if (childIds.Distinct().Count() != childIds.Length)
        {
            throw new ArgumentException(
                "A turn rotation cannot repeat a participant.",
                nameof(participantChildIds));
        }

        if (Array.IndexOf(childIds, firstChildId) < 0)
        {
            throw new ArgumentException(
                "The first child must be one of the participants.",
                nameof(firstChildId));
        }

        if (!Enum.IsDefined(advanceMode))
        {
            throw new ArgumentOutOfRangeException(nameof(advanceMode), "Unknown advance mode.");
        }

        Id = id;
        RotationId = rotationId;
        EffectiveFrom = effectiveFrom;
        Question = NormalizeQuestion(question);
        FirstChildId = firstChildId;
        AdvanceMode = advanceMode;
        CreatedByMemberId = createdByMemberId;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        for (var index = 0; index < childIds.Length; index++)
        {
            _participants.Add(new TurnRotationParticipant(childIds[index], index));
        }
    }

    /// <summary>
    /// A revision with no participants, recording that the rota is unavailable from
    /// <paramref name="effectiveFrom"/> until an adult configures it again.
    /// </summary>
    public static TurnRotationRevision Cleared(
        Guid id,
        Guid rotationId,
        DateOnly effectiveFrom,
        string question,
        Guid createdByMemberId,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || rotationId == Guid.Empty || createdByMemberId == Guid.Empty)
        {
            throw new ArgumentException("A cleared turn rotation revision needs an ID and creator ID.");
        }

        return new TurnRotationRevision
        {
            Id = id,
            RotationId = rotationId,
            EffectiveFrom = effectiveFrom,
            Question = NormalizeQuestion(question),
            FirstChildId = null,
            CreatedByMemberId = createdByMemberId,
            CreatedAtUtc = createdAtUtc.ToUniversalTime(),
        };
    }

    public Guid Id { get; private set; }

    /// <summary>Identifies which household rotation this revision belongs to.</summary>
    public Guid RotationId { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public string Question { get; private set; } = DefaultQuestion;

    public Guid? FirstChildId { get; private set; }

    public TurnAdvanceMode AdvanceMode { get; private set; } = TurnAdvanceMode.Daily;

    public bool IsCleared => _participants.Count == 0;

    /// <summary>
    /// Whether a tick moves the turn straight on to the other child the same day. Only a
    /// two-child rotation that moves on when ticked does; with one or three or more children a
    /// tick marks today's turn done and the next child's turn starts the following day.
    /// </summary>
    public bool MovesOnImmediately =>
        AdvanceMode == TurnAdvanceMode.WhenTicked && _participants.Count == 2;

    public Guid CreatedByMemberId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyList<TurnRotationParticipant> Participants => _participants;

    public IReadOnlyList<Guid> OrderedParticipantChildIds =>
        _participants.OrderBy(participant => participant.OrderIndex)
            .Select(participant => participant.ChildId)
            .ToArray();

    /// <summary>
    /// The child assigned on <paramref name="date"/> in a rotation that moves on every day,
    /// computed as the number of whole calendar days since <see cref="EffectiveFrom"/>,
    /// starting from <see cref="FirstChildId"/> and cycling through the ordered participants.
    /// </summary>
    public Guid? GetAssignedChildId(DateOnly date)
    {
        if (AdvanceMode != TurnAdvanceMode.Daily)
        {
            throw new InvalidOperationException(
                "A rotation that moves on when ticked needs its taken turns to answer.");
        }

        return GetTurn(date, date, [])?.ChildId;
    }

    /// <summary>
    /// Whose turn it is on <paramref name="date"/>, given the household-local
    /// <paramref name="today"/> and the rotation's taken turns.
    /// </summary>
    /// <remarks>
    /// A daily rotation advances once per calendar day and ignores ticks. A rotation that moves
    /// on when ticked advances once per tick of this revision: a date shows whose turn it was at
    /// the start of that day and whether a turn was ticked that day, except that today in a
    /// two-child rotation already reflects today's ticks. A future date shows the child whose
    /// turn it is now.
    /// </remarks>
    public TurnRotationTurn? GetTurn(
        DateOnly date,
        DateOnly today,
        IReadOnlyCollection<TakenTurn> takenTurns)
    {
        if (date < EffectiveFrom)
        {
            throw new ArgumentOutOfRangeException(
                nameof(date),
                "The date precedes this revision's effective date.");
        }

        var order = OrderedParticipantChildIds;
        if (order.Count == 0)
        {
            return null;
        }

        var startIndex = Array.IndexOf((Guid[])order, FirstChildId!.Value);
        if (AdvanceMode == TurnAdvanceMode.Daily)
        {
            var daysSinceEffective = date.DayNumber - EffectiveFrom.DayNumber;
            return new TurnRotationTurn(
                order[(startIndex + daysSinceEffective) % order.Count], false, daysSinceEffective);
        }

        var counted = CountedTurns(takenTurns);
        var takenBefore = counted.Count(turn => turn.Date < date);
        var takenOn = counted.Count(turn => turn.Date == date);
        var movedOnToday = date == today && MovesOnImmediately;
        var advanced = takenBefore + (movedOnToday ? takenOn : 0);
        return new TurnRotationTurn(
            order[(startIndex + advanced) % order.Count], !movedOnToday && takenOn > 0, advanced);
    }

    /// <summary>
    /// Ticks off <paramref name="childId"/>'s turn today. The tick must name the current turn
    /// by its <paramref name="turnNumber"/> as well as its child, so a repeated or stale tick
    /// never moves the rotation on twice — even when the same child's turn has come round
    /// again — and a rotation that is not moving on immediately can be ticked at most once a day.
    /// </summary>
    public TakenTurn TakeTurn(
        Guid id,
        Guid requestId,
        DateOnly today,
        int turnNumber,
        Guid childId,
        IReadOnlyCollection<TakenTurn> takenTurns,
        Guid recordedByMemberId,
        DateTimeOffset recordedAtUtc)
    {
        if (AdvanceMode != TurnAdvanceMode.WhenTicked)
        {
            throw new InvalidOperationException("Only a rotation that moves on when ticked can have turns ticked off.");
        }

        var turn = GetTurn(today, today, takenTurns)
            ?? throw new InvalidOperationException("A cleared rotation has no turns to tick off.");
        if (turn.IsTaken)
        {
            throw new InvalidOperationException("Today's turn has already been ticked off.");
        }

        if (turn.Number != turnNumber || turn.ChildId != childId)
        {
            throw new InvalidOperationException("That turn has already moved on.");
        }

        return new TakenTurn(
            id, requestId, RotationId, Id, turnNumber, today, childId, recordedByMemberId, recordedAtUtc);
    }

    /// <summary>
    /// The most recent tick of this revision that still counts, if any: the one that took the
    /// highest-numbered turn, since only the latest tick can be unticked and the counted ticks
    /// therefore always take turns 0, 1, 2… in order.
    /// </summary>
    public TakenTurn? LatestTakenTurn(IReadOnlyCollection<TakenTurn> takenTurns)
    {
        return CountedTurns(takenTurns)
            .OrderByDescending(turn => turn.TurnNumber)
            .FirstOrDefault();
    }

    private IEnumerable<TakenTurn> CountedTurns(IReadOnlyCollection<TakenTurn> takenTurns) =>
        takenTurns.Where(turn => turn.RevisionId == Id && !turn.IsUndone);

    private static string NormalizeQuestion(string? question)
    {
        var trimmed = question?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return DefaultQuestion;
        }

        if (trimmed.Length > MaximumQuestionLength)
        {
            throw new ArgumentException(
                $"A question cannot exceed {MaximumQuestionLength} characters.",
                nameof(question));
        }

        return trimmed;
    }
}

public sealed class TurnRotationParticipant
{
    private TurnRotationParticipant()
    {
    }

    internal TurnRotationParticipant(Guid childId, int orderIndex)
    {
        ChildId = childId;
        OrderIndex = orderIndex;
    }

    public Guid ChildId { get; private set; }

    public int OrderIndex { get; private set; }
}
