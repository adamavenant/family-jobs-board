namespace FamilyJobsBoard.Application.Today;

/// <summary>
/// The date range a family member may browse the daily agenda or calendar across. Bounds
/// externally supplied dates before they reach recurring-occurrence generation, which walks
/// every day between household today and the requested date — an unbounded date (or one near
/// <see cref="DateOnly.MaxValue"/>/<see cref="DateOnly.MinValue"/>) could otherwise generate an
/// unreasonable number of occurrences or overflow.
/// </summary>
internal static class BoardDateRange
{
    public const int MaxYearsFromToday = 2;

    public static bool IsWithinBrowsingHorizon(DateOnly date, DateOnly today)
    {
        return date >= today.AddYears(-MaxYearsFromToday)
            && date <= today.AddYears(MaxYearsFromToday);
    }
}
