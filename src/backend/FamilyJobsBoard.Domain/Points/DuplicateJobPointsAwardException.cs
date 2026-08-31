namespace FamilyJobsBoard.Domain.Points;

public sealed class DuplicateJobPointsAwardException : InvalidOperationException
{
    public DuplicateJobPointsAwardException()
        : base("Points have already been awarded for this job.")
    {
    }
}
