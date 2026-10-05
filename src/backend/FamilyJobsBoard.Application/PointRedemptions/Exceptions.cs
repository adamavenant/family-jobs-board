namespace FamilyJobsBoard.Application.PointRedemptions;

public sealed class InvalidPointRedemptionException : Exception
{
    public InvalidPointRedemptionException(IReadOnlyDictionary<string, string[]> errors)
        : base("The point redemption data was invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class PointRedemptionRequestConflictException : Exception
{
    public PointRedemptionRequestConflictException(Guid requestId)
        : base($"Request {requestId} was already used for a different point redemption.")
    {
    }
}

public sealed class DuplicatePointRedemptionRequestException : Exception
{
    public DuplicatePointRedemptionRequestException()
        : base("A point redemption with this request ID has already been recorded.")
    {
    }
}
