using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Application.Today;

namespace FamilyJobsBoard.Application.Calendar;

/// <summary>
/// Adult-only day/week/month browsing over the same job occurrences and workflow state as the
/// daily agenda. Authorization (adult-only) is enforced by the endpoint's policy, matching the
/// convention used by other adult-only write services; this service assumes the caller is an
/// authenticated household member and only validates the request's own shape.
/// </summary>
public sealed class CalendarService
{
    private readonly ITodayBoardRepository _repository;
    private readonly IHouseholdClock _clock;

    public CalendarService(ITodayBoardRepository repository, IHouseholdClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<CalendarBoard> GetAsync(
        Guid viewerId,
        string? view,
        DateOnly? date,
        Guid? childId,
        CancellationToken cancellationToken)
    {
        var members = await _repository.GetMembersAsync(cancellationToken);
        if (members.Count == 0)
        {
            throw new TodayBoardNotAvailableException();
        }

        var viewer = members.SingleOrDefault(member => member.Id == viewerId)
            ?? throw new HouseholdMemberNotFoundException(viewerId);
        var children = members.Where(member => !member.IsAdult).ToArray();

        var errors = new Dictionary<string, string[]>();
        if (!TryParseView(view, out var calendarView))
        {
            errors["View"] = ["Choose day, week, or month."];
        }

        var selectedChild = childId is null
            ? null
            : children.SingleOrDefault(child => child.Id == childId.Value);
        if (childId is not null && selectedChild is null)
        {
            errors["ChildId"] = ["Choose an active child in this household."];
        }

        if (errors.Count > 0)
        {
            throw new InvalidCalendarRequestException(errors);
        }

        var anchorDate = date ?? _clock.Today;
        var (rangeStart, rangeEnd) = ComputeRange(calendarView, anchorDate);

        await RecurringOccurrenceGenerator.EnsureGeneratedThroughAsync(
            _repository,
            _clock,
            rangeEnd,
            cancellationToken);

        var visibleChildIds = selectedChild is null
            ? children.Select(child => child.Id).ToArray()
            : [selectedChild.Id];
        var jobs = await _repository.GetJobsInRangeAsync(
            visibleChildIds,
            rangeStart,
            rangeEnd,
            cancellationToken);
        var rejections = await _repository.GetLatestRejectionsInRangeAsync(
            visibleChildIds,
            rangeStart,
            rangeEnd,
            cancellationToken);
        var rejectionByJobId = rejections.ToDictionary(rejection => rejection.JobId);
        var childById = children.ToDictionary(child => child.Id);
        var jobsByDate = jobs
            .GroupBy(job => job.ScheduledDate)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(job => TodayJobMapping.MapJob(
                        job,
                        childById[job.ChildId],
                        rejectionByJobId.GetValueOrDefault(job.Id)))
                    .ToArray());

        var focusedMonth = new DateOnly(anchorDate.Year, anchorDate.Month, 1);
        var days = new List<CalendarDay>();
        for (var current = rangeStart; current <= rangeEnd; current = current.AddDays(1))
        {
            var isInFocusedPeriod = calendarView != CalendarView.Month
                || (current.Year == focusedMonth.Year && current.Month == focusedMonth.Month);
            days.Add(new CalendarDay(
                current,
                isInFocusedPeriod,
                jobsByDate.GetValueOrDefault(current, [])));
        }

        return new CalendarBoard(
            TodayJobMapping.MapMember(viewer),
            members.Select(TodayJobMapping.MapMember).ToArray(),
            calendarView,
            anchorDate,
            _clock.Today,
            rangeStart,
            rangeEnd,
            selectedChild?.Id,
            days);
    }

    private static bool TryParseView(string? view, out CalendarView calendarView)
    {
        switch (view)
        {
            case "day":
                calendarView = CalendarView.Day;
                return true;
            case "week":
                calendarView = CalendarView.Week;
                return true;
            case "month":
                calendarView = CalendarView.Month;
                return true;
            default:
                calendarView = default;
                return false;
        }
    }

    private static (DateOnly Start, DateOnly End) ComputeRange(CalendarView view, DateOnly anchorDate)
    {
        return view switch
        {
            CalendarView.Day => (anchorDate, anchorDate),
            CalendarView.Week => WeekRange(anchorDate),
            CalendarView.Month => MonthGridRange(anchorDate),
            _ => throw new ArgumentOutOfRangeException(nameof(view)),
        };
    }

    private static (DateOnly Start, DateOnly End) WeekRange(DateOnly anchorDate)
    {
        var start = StartOfWeek(anchorDate);
        return (start, start.AddDays(6));
    }

    private static (DateOnly Start, DateOnly End) MonthGridRange(DateOnly anchorDate)
    {
        var firstOfMonth = new DateOnly(anchorDate.Year, anchorDate.Month, 1);
        var gridStart = StartOfWeek(firstOfMonth);

        // A fixed six-week (42-day) grid keeps the calendar's shape stable across months.
        return (gridStart, gridStart.AddDays(41));
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        // ISO-8601: the week starts on Monday. DateOnly.DayOfWeek is Sunday=0..Saturday=6.
        var offsetFromMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offsetFromMonday);
    }
}
