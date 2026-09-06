using FamilyJobsBoard.Domain.Households;

namespace FamilyJobsBoard.Domain.Identity;

public static class PinPolicy
{
    public static bool IsValid(string? pin, HouseholdRole role)
    {
        var length = role == HouseholdRole.Adult ? 6 : 4;
        return pin is not null
            && pin.Length == length
            && pin.All(character => character is >= '0' and <= '9');
    }
}
