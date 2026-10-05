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
