namespace FamilyJobsBoard.Domain.Points;

public sealed class DuplicateJobApprovalReversalException : InvalidOperationException
{
    public DuplicateJobApprovalReversalException()
        : base("This job's approval has already been reversed.")
    {
    }
}
