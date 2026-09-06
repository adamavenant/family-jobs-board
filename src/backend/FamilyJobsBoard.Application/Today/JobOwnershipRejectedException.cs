namespace FamilyJobsBoard.Application.Today;

public sealed class JobOwnershipRejectedException : Exception
{
    public JobOwnershipRejectedException()
        : base("A child can complete only their own job.")
    {
    }
}
