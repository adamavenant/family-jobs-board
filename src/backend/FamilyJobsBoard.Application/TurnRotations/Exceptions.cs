namespace FamilyJobsBoard.Application.TurnRotations;

public sealed class InvalidTurnRotationException : Exception
{
    public InvalidTurnRotationException(IReadOnlyDictionary<string, string[]> errors)
        : base("The turn rotation data was invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class TurnRotationForbiddenException : Exception
{
    public TurnRotationForbiddenException()
        : base("Only an adult in this household can configure the turn rotation.")
    {
    }
}
