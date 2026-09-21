namespace FamilyJobsBoard.Domain.GoodBehaviours;

public sealed class GoodBehaviourTypeInactiveException : InvalidOperationException
{
    public GoodBehaviourTypeInactiveException(Guid typeId)
        : base($"Good behaviour type {typeId} has been deleted and can no longer be changed.")
    {
    }
}
