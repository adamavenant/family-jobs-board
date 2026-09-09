namespace FamilyJobsBoard.Domain.Households;

public sealed class HouseholdMember
{
    public const int MaximumNameLength = 100;

    private HouseholdMember()
    {
    }

    public HouseholdMember(
        Guid id,
        string firstName,
        bool isAdult,
        string? nickname = null,
        bool isActive = true)
        : this(
            id,
            firstName,
            null,
            isAdult ? HouseholdRole.Adult : HouseholdRole.Child,
            nickname,
            isActive)
    {
    }

    public HouseholdMember(
        Guid id,
        string firstName,
        string? surname,
        HouseholdRole role,
        string? nickname = null,
        bool isActive = true)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A household member needs an ID.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new ArgumentException("A household member needs a first name.", nameof(firstName));
        }

        var trimmedFirstName = firstName.Trim();
        if (trimmedFirstName.Length > MaximumNameLength)
        {
            throw new ArgumentException(
                $"A first name cannot exceed {MaximumNameLength} characters.",
                nameof(firstName));
        }

        var trimmedNickname = nickname?.Trim();
        if (trimmedNickname?.Length > MaximumNameLength)
        {
            throw new ArgumentException(
                $"A nickname cannot exceed {MaximumNameLength} characters.",
                nameof(nickname));
        }

        Id = id;
        FirstName = trimmedFirstName;
        Surname = NormalizeOptionalName(surname, nameof(surname));
        Nickname = string.IsNullOrEmpty(trimmedNickname) ? null : trimmedNickname;
        Role = role;
        IsActive = isActive;
    }

    public Guid Id { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public string? Surname { get; private set; }

    public string? Nickname { get; private set; }

    public string DisplayName => Nickname ?? FirstName;

    public HouseholdRole Role { get; private set; }

    public bool IsAdult => Role == HouseholdRole.Adult;

    public bool IsActive { get; private set; }

    public DateTimeOffset? ProfileUpdatedAtUtc { get; private set; }

    public Guid? ProfileUpdatedByMemberId { get; private set; }

    public DateTimeOffset? DeactivatedAtUtc { get; private set; }

    public Guid? DeactivatedByMemberId { get; private set; }

    public DateTimeOffset? RestoredAtUtc { get; private set; }

    public Guid? RestoredByMemberId { get; private set; }

    public void UpdateProfile(
        string firstName,
        string surname,
        string? nickname,
        Guid actorMemberId,
        DateTimeOffset now)
    {
        EnsureActor(actorMemberId);
        FirstName = NormalizeRequiredName(firstName, nameof(firstName));
        Surname = NormalizeRequiredName(surname, nameof(surname));
        Nickname = NormalizeOptionalName(nickname, nameof(nickname));
        ProfileUpdatedAtUtc = now;
        ProfileUpdatedByMemberId = actorMemberId;
    }

    public void Deactivate(Guid actorMemberId, DateTimeOffset now)
    {
        EnsureActor(actorMemberId);
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        DeactivatedAtUtc = now;
        DeactivatedByMemberId = actorMemberId;
    }

    public void Restore(Guid actorMemberId, DateTimeOffset now)
    {
        EnsureActor(actorMemberId);
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        RestoredAtUtc = now;
        RestoredByMemberId = actorMemberId;
    }

    public void SetSurname(string surname)
    {
        Surname = NormalizeRequiredName(surname, nameof(surname));
    }

    private static string NormalizeRequiredName(string value, string parameterName) =>
        NormalizeOptionalName(value, parameterName)
        ?? throw new ArgumentException("A name is required.", parameterName);

    private static void EnsureActor(Guid actorMemberId)
    {
        if (actorMemberId == Guid.Empty)
        {
            throw new ArgumentException("A profile change needs an actor ID.", nameof(actorMemberId));
        }
    }

    private static string? NormalizeOptionalName(string? value, string parameterName)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > MaximumNameLength)
        {
            throw new ArgumentException(
                $"A name cannot exceed {MaximumNameLength} characters.",
                parameterName);
        }

        return trimmed;
    }
}
