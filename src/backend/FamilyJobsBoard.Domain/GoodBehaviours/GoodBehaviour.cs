namespace FamilyJobsBoard.Domain.GoodBehaviours;

/// <summary>
/// An immutable record of a good behaviour observed by an adult. The type's name and
/// description are copied at logging time so later edits or deletion of the type never
/// change history.
/// </summary>
public sealed class GoodBehaviour
{
    private GoodBehaviour()
    {
    }

    public GoodBehaviour(
        Guid id,
        Guid requestId,
        GoodBehaviourType type,
        Guid childId,
        Guid loggedByMemberId,
        int points,
        DateTimeOffset loggedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A good behaviour needs an ID.", nameof(id));
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("A good behaviour needs a request ID.", nameof(requestId));
        }

        if (childId == Guid.Empty)
        {
            throw new ArgumentException("A good behaviour needs a child.", nameof(childId));
        }

        if (loggedByMemberId == Guid.Empty)
        {
            throw new ArgumentException("A good behaviour needs a logging adult.", nameof(loggedByMemberId));
        }

        if (points < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(points), "Points cannot be negative.");
        }

        Id = id;
        RequestId = requestId;
        TypeId = type.Id;
        TypeName = type.Name;
        TypeDescription = type.Description;
        ChildId = childId;
        LoggedByMemberId = loggedByMemberId;
        Points = points;
        LoggedAtUtc = loggedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public Guid TypeId { get; private set; }

    public string TypeName { get; private set; } = string.Empty;

    public string TypeDescription { get; private set; } = string.Empty;

    public Guid ChildId { get; private set; }

    public Guid LoggedByMemberId { get; private set; }

    public int Points { get; private set; }

    public DateTimeOffset LoggedAtUtc { get; private set; }
}
