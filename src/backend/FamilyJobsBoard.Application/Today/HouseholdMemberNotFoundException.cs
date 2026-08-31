namespace FamilyJobsBoard.Application.Today;

public sealed class HouseholdMemberNotFoundException : Exception
{
    public HouseholdMemberNotFoundException(Guid memberId)
        : base($"Household member '{memberId}' was not found.")
    {
    }
}
