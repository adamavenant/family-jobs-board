namespace FamilyJobsBoard.Domain.Households;

public sealed class HouseholdMember
{
    private HouseholdMember()
    {
    }

    public HouseholdMember(Guid id, string firstName, bool isAdult)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A household member needs an ID.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new ArgumentException("A household member needs a first name.", nameof(firstName));
        }

        Id = id;
        FirstName = firstName.Trim();
        IsAdult = isAdult;
    }

    public Guid Id { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public bool IsAdult { get; private set; }
}
