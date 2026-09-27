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
