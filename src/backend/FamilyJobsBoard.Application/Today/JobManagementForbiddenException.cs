namespace FamilyJobsBoard.Application.Today;

public sealed class JobManagementForbiddenException : Exception
{
    public JobManagementForbiddenException()
        : base("Only an adult in this household can edit or cancel jobs.")
    {
    }
}
