namespace FamilyJobsBoard.Application.PointAdjustments;

public sealed class InvalidPointAdjustmentException : Exception
{
    public InvalidPointAdjustmentException(IReadOnlyDictionary<string, string[]> errors)
        : base("The point adjustment data was invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class PointAdjustmentRequestConflictException : Exception
{
    public PointAdjustmentRequestConflictException(Guid requestId)
        : base($"Request {requestId} was already used for a different point adjustment.")
    {
    }
}

public sealed class DuplicatePointAdjustmentRequestException : Exception
{
    public DuplicatePointAdjustmentRequestException()
        : base("A point adjustment with this request ID has already been recorded.")
    {
    }
}

public sealed class NegativeBalanceConfirmationRequiredException : Exception
{
    public NegativeBalanceConfirmationRequiredException(
        string childDisplayName,
        int currentBalance,
        int resultingBalance)
        : base(
            $"This adjustment would take {childDisplayName}'s balance from {currentBalance} to {resultingBalance}. Confirm to continue.")
    {
        CurrentBalance = currentBalance;
        ResultingBalance = resultingBalance;
    }

    public int CurrentBalance { get; }

    public int ResultingBalance { get; }
}
