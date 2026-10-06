namespace FamilyJobsBoard.Application.Points;

public sealed class InvalidPointsLedgerRequestException : Exception
{
    public InvalidPointsLedgerRequestException(IReadOnlyDictionary<string, string[]> errors)
        : base("The points ledger request was invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>
/// The viewer may not read the requested ledger: a child asked for another child's points,
/// or the viewer is no longer an active household member.
/// </summary>
public sealed class PointsLedgerForbiddenException : Exception
{
    public PointsLedgerForbiddenException()
        : base("You can't view these points.")
    {
    }
}

/// <summary>
/// Removing the requested points would take the child's balance below zero.
/// </summary>
public sealed class InsufficientPointsException : Exception
{
    public InsufficientPointsException(string childDisplayName, int currentBalance)
        : base(currentBalance switch
        {
            <= 0 => $"{childDisplayName} doesn't have any points.",
            1 => $"{childDisplayName} only has 1 point.",
            _ => $"{childDisplayName} only has {currentBalance} points.",
        })
    {
        CurrentBalance = currentBalance;
    }

    public int CurrentBalance { get; }
}
