using FamilyJobsBoard.Api.Features.Today;

namespace FamilyJobsBoard.Api.Features.Calendar;

public sealed record CalendarResponse(
    MemberResponse Viewer,
    IReadOnlyList<MemberResponse> Members,
    string View,
    DateOnly AnchorDate,
    DateOnly CurrentDate,
    DateOnly RangeStart,
    DateOnly RangeEnd,
    Guid? SelectedChildId,
    IReadOnlyList<CalendarDayResponse> Days);

public sealed record CalendarDayResponse(
    DateOnly Date,
    bool IsInFocusedPeriod,
    IReadOnlyList<JobResponse> Jobs);
