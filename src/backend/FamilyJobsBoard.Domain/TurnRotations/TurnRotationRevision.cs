namespace FamilyJobsBoard.Domain.TurnRotations;

/// <summary>
/// An immutable, effective-dated configuration of the household's daily "whose turn"
/// rotation. A new revision is created whenever an adult reconfigures the rotation;
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
        DateOnly effectiveFrom,
        string? question,
        IReadOnlyList<Guid> participantChildIds,
        Guid firstChildId,
        Guid createdByMemberId,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A turn rotation revision needs an ID.", nameof(id));
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

        Id = id;
        EffectiveFrom = effectiveFrom;
        Question = NormalizeQuestion(question);
        FirstChildId = firstChildId;
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
        DateOnly effectiveFrom,
        string question,
        Guid createdByMemberId,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || createdByMemberId == Guid.Empty)
        {
            throw new ArgumentException("A cleared turn rotation revision needs an ID and creator ID.");
        }

        return new TurnRotationRevision
        {
            Id = id,
            EffectiveFrom = effectiveFrom,
            Question = NormalizeQuestion(question),
            FirstChildId = null,
            CreatedByMemberId = createdByMemberId,
            CreatedAtUtc = createdAtUtc.ToUniversalTime(),
        };
    }

    public Guid Id { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public string Question { get; private set; } = DefaultQuestion;

    public Guid? FirstChildId { get; private set; }

    public bool IsCleared => _participants.Count == 0;

    public Guid CreatedByMemberId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyList<TurnRotationParticipant> Participants => _participants;

    public IReadOnlyList<Guid> OrderedParticipantChildIds =>
        _participants.OrderBy(participant => participant.OrderIndex)
            .Select(participant => participant.ChildId)
            .ToArray();

    /// <summary>
    /// The child assigned on <paramref name="date"/>, computed as the number of whole
    /// calendar days since <see cref="EffectiveFrom"/>, starting from <see cref="FirstChildId"/>
    /// and cycling through the ordered participants.
    /// </summary>
    public Guid? GetAssignedChildId(DateOnly date)
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
        var daysSinceEffective = date.DayNumber - EffectiveFrom.DayNumber;
        var index = (startIndex + daysSinceEffective) % order.Count;
        return order[index];
    }

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
