namespace FamilyJobsBoard.Application.Today;

public sealed class TodayBoardNotAvailableException : InvalidOperationException
{
    public TodayBoardNotAvailableException()
        : base("The demo child has not been configured.")
    {
    }
}
