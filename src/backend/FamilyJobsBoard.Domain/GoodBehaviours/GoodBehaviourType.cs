namespace FamilyJobsBoard.Domain.GoodBehaviours;

public sealed class GoodBehaviourType
{
    public const int MaximumNameLength = 100;
    public const int MaximumDescriptionLength = 500;

    private GoodBehaviourType()
    {
    }

    public GoodBehaviourType(
        Guid id,
        string name,
        string description,
        int points,
        Guid createdByMemberId,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A good behaviour type needs an ID.", nameof(id));
        }

        EnsureActor(createdByMemberId, nameof(createdByMemberId));

        Id = id;
        Name = NormalizeName(name);
        Description = NormalizeDescription(description);
        Points = EnsurePoints(points);
        IsActive = true;
        CreatedByMemberId = createdByMemberId;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public int Points { get; private set; }

    public bool IsActive { get; private set; }

    public Guid CreatedByMemberId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? UpdatedByMemberId { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public Guid? DeactivatedByMemberId { get; private set; }

    public DateTimeOffset? DeactivatedAtUtc { get; private set; }

    public void Update(
        string name,
        string description,
        int points,
        Guid actorMemberId,
        DateTimeOffset now)
    {
        EnsureActor(actorMemberId, nameof(actorMemberId));
        if (!IsActive)
        {
            throw new GoodBehaviourTypeInactiveException(Id);
        }

        Name = NormalizeName(name);
        Description = NormalizeDescription(description);
        Points = EnsurePoints(points);
        UpdatedByMemberId = actorMemberId;
        UpdatedAtUtc = now.ToUniversalTime();
    }

    public void Deactivate(Guid actorMemberId, DateTimeOffset now)
    {
        EnsureActor(actorMemberId, nameof(actorMemberId));
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        DeactivatedByMemberId = actorMemberId;
        DeactivatedAtUtc = now.ToUniversalTime();
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A good behaviour type needs a name.", nameof(name));
        }

        var trimmed = name.Trim();
        if (trimmed.Length > MaximumNameLength)
        {
            throw new ArgumentException(
                $"A good behaviour type name cannot exceed {MaximumNameLength} characters.",
                nameof(name));
        }

        return trimmed;
    }

    private static string NormalizeDescription(string description)
    {
        var trimmed = (description ?? string.Empty).Trim();
        if (trimmed.Length > MaximumDescriptionLength)
        {
            throw new ArgumentException(
                $"A good behaviour type description cannot exceed {MaximumDescriptionLength} characters.",
                nameof(description));
        }

        return trimmed;
    }

    private static int EnsurePoints(int points)
    {
        if (points < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(points), "Points cannot be negative.");
        }

        return points;
    }

    private static void EnsureActor(Guid actorMemberId, string parameterName)
    {
        if (actorMemberId == Guid.Empty)
        {
            throw new ArgumentException("A good behaviour type change needs an actor ID.", parameterName);
        }
    }
}
