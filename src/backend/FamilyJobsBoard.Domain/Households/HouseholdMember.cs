namespace FamilyJobsBoard.Domain.Households;

public sealed class HouseholdMember
{
    public const int MaximumNameLength = 100;

    private HouseholdMember()
    {
    }

    public HouseholdMember(Guid id, string firstName, bool isAdult, string? nickname = null)
        : this(id, firstName, null, isAdult ? HouseholdRole.Adult : HouseholdRole.Child, nickname)
    {
    }

    public HouseholdMember(
        Guid id,
        string firstName,
        string? surname,
        HouseholdRole role,
        string? nickname = null)
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
    }

    public Guid Id { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public string? Surname { get; private set; }

    public string? Nickname { get; private set; }

    public string DisplayName => Nickname ?? FirstName;

    public HouseholdRole Role { get; private set; }

    public bool IsAdult => Role == HouseholdRole.Adult;

    public void SetSurname(string surname)
    {
        Surname = NormalizeOptionalName(surname, nameof(surname))
            ?? throw new ArgumentException("A surname is required.", nameof(surname));
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
