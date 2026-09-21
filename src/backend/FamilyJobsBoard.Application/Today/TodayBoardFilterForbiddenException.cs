namespace FamilyJobsBoard.Application.Today;

public sealed class TodayBoardFilterForbiddenException : Exception
{
    public TodayBoardFilterForbiddenException()
        : base("Only an adult can filter the household daily board.")
    {
    }
}
