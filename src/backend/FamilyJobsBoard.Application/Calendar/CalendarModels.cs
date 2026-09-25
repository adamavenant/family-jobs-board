using FamilyJobsBoard.Application.Today;

namespace FamilyJobsBoard.Application.Calendar;

public enum CalendarView
{
    Day,
    Week,
    Month,
}

public sealed record CalendarBoard(
    TodayMember Viewer,
    IReadOnlyList<TodayMember> Members,
    CalendarView View,
    DateOnly AnchorDate,
    DateOnly CurrentDate,
    DateOnly RangeStart,
    DateOnly RangeEnd,
    Guid? SelectedChildId,
    IReadOnlyList<CalendarDay> Days);

public sealed record CalendarDay(
    DateOnly Date,
    bool IsInFocusedPeriod,
    IReadOnlyList<TodayJob> Jobs);
