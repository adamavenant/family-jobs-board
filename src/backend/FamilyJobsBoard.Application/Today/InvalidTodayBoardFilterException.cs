namespace FamilyJobsBoard.Application.Today;

public sealed class InvalidTodayBoardFilterException : Exception
{
    public InvalidTodayBoardFilterException(IReadOnlyDictionary<string, string[]> errors)
        : base("The daily board filter is invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
