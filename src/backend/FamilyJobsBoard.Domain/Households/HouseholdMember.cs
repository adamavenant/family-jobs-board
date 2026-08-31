namespace FamilyJobsBoard.Domain.Households;

public sealed class HouseholdMember
{
    public const int MaximumNameLength = 100;

    private HouseholdMember()
    {
    }

    public HouseholdMember(Guid id, string firstName, bool isAdult, string? nickname = null)
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
        Nickname = string.IsNullOrEmpty(trimmedNickname) ? null : trimmedNickname;
        IsAdult = isAdult;
    }

    public Guid Id { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public string? Nickname { get; private set; }

    public string DisplayName => Nickname ?? FirstName;

    public bool IsAdult { get; private set; }
}
