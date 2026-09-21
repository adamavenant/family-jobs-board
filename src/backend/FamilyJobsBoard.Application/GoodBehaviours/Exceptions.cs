namespace FamilyJobsBoard.Application.GoodBehaviours;

public sealed class InvalidGoodBehaviourException : Exception
{
    public InvalidGoodBehaviourException(IReadOnlyDictionary<string, string[]> errors)
        : base("The good behaviour data was invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class GoodBehaviourTypeNotFoundException : Exception
{
    public GoodBehaviourTypeNotFoundException(Guid typeId)
        : base($"Good behaviour type {typeId} was not found.")
    {
    }
}

public sealed class GoodBehaviourRequestConflictException : Exception
{
    public GoodBehaviourRequestConflictException(Guid requestId)
        : base($"Request {requestId} was already used for a different good behaviour.")
    {
    }
}

public sealed class DuplicateGoodBehaviourRequestException : Exception
{
    public DuplicateGoodBehaviourRequestException()
        : base("A good behaviour with this request ID has already been logged.")
    {
    }
}
