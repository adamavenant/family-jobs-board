namespace FamilyJobsBoard.Application.Today;

/// <summary>
/// Reversing the approval would take back more points than the child has left, for example
/// because they were already redeemed. Points never go below zero, so nothing changes.
/// </summary>
public sealed class ApprovalReversalInsufficientPointsException : Exception
{
    public ApprovalReversalInsufficientPointsException(
        string childDisplayName,
        int currentBalance,
        int awardedPoints)
        : base(
            $"{Has(childDisplayName, currentBalance)}, so reversing this approval can't take back "
            + $"its {Points(awardedPoints)}. Nothing was changed.")
    {
        CurrentBalance = currentBalance;
    }

    public int CurrentBalance { get; }

    private static string Has(string childDisplayName, int balance) => balance <= 0
        ? $"{childDisplayName} doesn't have any points left"
        : $"{childDisplayName} only has {Points(balance)}";

    private static string Points(int points) => points == 1 ? "1 point" : $"{points} points";
}
