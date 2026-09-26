using FamilyJobsBoard.Application.Calendar;
using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Application.Today;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Domain.Points;
using Xunit;

namespace FamilyJobsBoard.Application.Tests;

public sealed class CalendarServiceTests
{
    // A Tuesday, so week/month range math is exercised against a non-boundary anchor.
    private static readonly DateOnly Today = new(2026, 9, 22);
    private static readonly HouseholdMember Adult = new(
        Guid.Parse("d55b77d7-a514-4071-a004-f5033aff2f9d"),
        "Addie",
        "Avenant",
        HouseholdRole.Adult);
    private static readonly HouseholdMember FirstChild = new(
        Guid.Parse("96e7d927-dfb6-480c-89be-a5c58144f603"),
        "Fredster",
        "Avenant",
        HouseholdRole.Child);
    private static readonly HouseholdMember SecondChild = new(
        Guid.Parse("3fdb1469-df09-420e-8eeb-330717c710fe"),
        "Harrie",
        "Avenant",
        HouseholdRole.Child);

    [Fact]
    public async Task Day_view_defaults_to_today_and_returns_a_single_day()
    {
        var repository = new FakeRepository([Adult, FirstChild]);
        var service = new CalendarService(repository, new FixedClock());

        var board = await service.GetAsync(Adult.Id, "day", null, null, CancellationToken.None);

        Assert.Equal(CalendarView.Day, board.View);
        Assert.Equal(Today, board.AnchorDate);
        Assert.Equal(Today, board.CurrentDate);
        Assert.Equal(Today, board.RangeStart);
        Assert.Equal(Today, board.RangeEnd);
        var day = Assert.Single(board.Days);
        Assert.Equal(Today, day.Date);
        Assert.True(day.IsInFocusedPeriod);
    }

    [Fact]
    public async Task Week_view_starts_on_monday_and_spans_seven_days()
    {
        var repository = new FakeRepository([Adult, FirstChild]);
        var service = new CalendarService(repository, new FixedClock());

        var board = await service.GetAsync(Adult.Id, "week", Today, null, CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 9, 21), board.RangeStart); // Monday
        Assert.Equal(new DateOnly(2026, 9, 27), board.RangeEnd); // Sunday
        Assert.Equal(7, board.Days.Count);
        Assert.Equal(DayOfWeek.Monday, board.Days[0].Date.DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, board.Days[^1].Date.DayOfWeek);
        Assert.All(board.Days, day => Assert.True(day.IsInFocusedPeriod));
    }

    [Fact]
    public async Task Week_view_anchored_on_a_sunday_still_starts_on_the_same_weeks_monday()
    {
        var repository = new FakeRepository([Adult, FirstChild]);
        var service = new CalendarService(repository, new FixedClock());
        var sunday = new DateOnly(2026, 9, 27);

        var board = await service.GetAsync(Adult.Id, "week", sunday, null, CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 9, 21), board.RangeStart);
        Assert.Equal(sunday, board.RangeEnd);
    }

    [Fact]
    public async Task Month_view_is_a_fixed_six_week_grid_with_adjacent_month_days_unfocused()
    {
        var repository = new FakeRepository([Adult, FirstChild]);
        var service = new CalendarService(repository, new FixedClock());

        var board = await service.GetAsync(Adult.Id, "month", Today, null, CancellationToken.None);

        Assert.Equal(42, board.Days.Count);
        Assert.Equal(DayOfWeek.Monday, board.Days[0].Date.DayOfWeek);
        Assert.True(board.RangeStart <= new DateOnly(2026, 9, 1));
        Assert.True(board.RangeEnd >= new DateOnly(2026, 9, 30));
        Assert.All(
            board.Days.Where(day => day.Date.Month == 9 && day.Date.Year == 2026),
            day => Assert.True(day.IsInFocusedPeriod));
        Assert.Contains(board.Days, day => day.Date.Month != 9 && !day.IsInFocusedPeriod);
    }

    [Fact]
    public async Task February_in_a_leap_year_still_produces_exactly_forty_two_days()
    {
        var repository = new FakeRepository([Adult, FirstChild]);
        var service = new CalendarService(repository, new FixedClock());

        var board = await service.GetAsync(
            Adult.Id, "month", new DateOnly(2028, 2, 10), null, CancellationToken.None);

        Assert.Equal(42, board.Days.Count);
        Assert.True(board.RangeStart <= new DateOnly(2028, 2, 1));
        Assert.True(board.RangeEnd >= new DateOnly(2028, 2, 29));
    }

    [Fact]
    public async Task Jobs_are_grouped_onto_their_scheduled_date_within_the_range()
    {
        var repository = new FakeRepository([Adult, FirstChild, SecondChild]);
        repository.Jobs.AddRange(
        [
            new Job(Guid.NewGuid(), FirstChild.Id, "In range", "", 1, Today.AddDays(1)),
            new Job(Guid.NewGuid(), SecondChild.Id, "Also in range", "", 1, Today.AddDays(1)),
            new Job(Guid.NewGuid(), FirstChild.Id, "Out of range", "", 1, Today.AddMonths(2)),
        ]);
        var service = new CalendarService(repository, new FixedClock());

        var board = await service.GetAsync(Adult.Id, "week", Today, null, CancellationToken.None);

        var day = board.Days.Single(item => item.Date == Today.AddDays(1));
        Assert.Equal(2, day.Jobs.Count);
        Assert.All(board.Days.Where(item => item.Date != Today.AddDays(1)), item => Assert.Empty(item.Jobs));
    }

    [Fact]
    public async Task Past_incomplete_jobs_still_appear_on_their_originally_assigned_date()
    {
        var repository = new FakeRepository([Adult, FirstChild]);
        var pastDate = Today.AddDays(-10);
        repository.Jobs.Add(new Job(Guid.NewGuid(), FirstChild.Id, "Overdue", "", 1, pastDate));
        var service = new CalendarService(repository, new FixedClock());

        var board = await service.GetAsync(Adult.Id, "month", pastDate, null, CancellationToken.None);

        var day = board.Days.Single(item => item.Date == pastDate);
        var job = Assert.Single(day.Jobs);
        Assert.Equal("Overdue", job.Name);
        Assert.Equal("open", job.Status);
    }

    [Fact]
    public async Task Child_filter_limits_jobs_to_that_child_and_is_echoed_back()
    {
        var repository = new FakeRepository([Adult, FirstChild, SecondChild]);
        repository.Jobs.AddRange(
        [
            new Job(Guid.NewGuid(), FirstChild.Id, "Fredster's", "", 1, Today),
            new Job(Guid.NewGuid(), SecondChild.Id, "Harrie's", "", 1, Today),
        ]);
        var service = new CalendarService(repository, new FixedClock());

        var board = await service.GetAsync(
            Adult.Id, "day", Today, SecondChild.Id, CancellationToken.None);

        Assert.Equal(SecondChild.Id, board.SelectedChildId);
        var job = Assert.Single(Assert.Single(board.Days).Jobs);
        Assert.Equal("Harrie's", job.Name);
    }

    [Fact]
    public async Task Unknown_child_filter_is_rejected()
    {
        var repository = new FakeRepository([Adult, FirstChild]);
        var service = new CalendarService(repository, new FixedClock());

        var exception = await Assert.ThrowsAsync<InvalidCalendarRequestException>(() =>
            service.GetAsync(Adult.Id, "day", Today, Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("ChildId", exception.Errors.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fortnight")]
    public async Task Invalid_view_is_rejected(string? view)
    {
        var repository = new FakeRepository([Adult, FirstChild]);
        var service = new CalendarService(repository, new FixedClock());

        var exception = await Assert.ThrowsAsync<InvalidCalendarRequestException>(() =>
            service.GetAsync(Adult.Id, view, Today, null, CancellationToken.None));

        Assert.Contains("View", exception.Errors.Keys);
    }

    [Theory]
    [InlineData(3, 0)] // three years in the future
    [InlineData(-3, 0)] // three years in the past
    public async Task A_date_far_outside_the_browsing_horizon_is_rejected(int years, int days)
    {
        var repository = new FakeRepository([Adult, FirstChild]);
        var service = new CalendarService(repository, new FixedClock());
        var farDate = Today.AddYears(years).AddDays(days);

        var exception = await Assert.ThrowsAsync<InvalidCalendarRequestException>(() =>
            service.GetAsync(Adult.Id, "day", farDate, null, CancellationToken.None));

        Assert.Contains("Date", exception.Errors.Keys);
        Assert.Null(repository.LastGenerationHorizon);
    }

    [Fact]
    public async Task A_date_at_the_edge_of_the_browsing_horizon_is_accepted()
    {
        var repository = new FakeRepository([Adult, FirstChild]);
        var service = new CalendarService(repository, new FixedClock());

        var future = await service.GetAsync(
            Adult.Id, "day", Today.AddYears(2), null, CancellationToken.None);
        var past = await service.GetAsync(
            Adult.Id, "day", Today.AddYears(-2), null, CancellationToken.None);

        Assert.Equal(Today.AddYears(2), future.AnchorDate);
        Assert.Equal(Today.AddYears(-2), past.AnchorDate);
    }

    [Fact]
    public async Task An_out_of_range_date_near_DateOnly_MaxValue_does_not_overflow()
    {
        var repository = new FakeRepository([Adult, FirstChild]);
        var service = new CalendarService(repository, new FixedClock());

        var exception = await Assert.ThrowsAsync<InvalidCalendarRequestException>(() =>
            service.GetAsync(Adult.Id, "month", DateOnly.MaxValue, null, CancellationToken.None));

        Assert.Contains("Date", exception.Errors.Keys);
    }

    [Fact]
    public async Task Generation_horizon_never_shrinks_below_the_standard_rolling_window()
    {
        var repository = new FakeRepository([Adult, FirstChild]);
        var service = new CalendarService(repository, new FixedClock());

        var board = await service.GetAsync(Adult.Id, "month", Today, null, CancellationToken.None);

        Assert.True(board.RangeEnd < Today.AddDays(55));
        Assert.Equal(Today.AddDays(55), repository.LastGenerationHorizon);
    }

    [Fact]
    public async Task A_calendar_range_beyond_the_rolling_window_extends_the_generation_horizon()
    {
        var repository = new FakeRepository([Adult, FirstChild]);
        var service = new CalendarService(repository, new FixedClock());
        var farAnchor = Today.AddDays(200);

        var board = await service.GetAsync(Adult.Id, "week", farAnchor, null, CancellationToken.None);

        Assert.Equal(board.RangeEnd, repository.LastGenerationHorizon);
    }

    [Fact]
    public async Task Unknown_viewer_is_reported()
    {
        var repository = new FakeRepository([Adult, FirstChild]);
        var service = new CalendarService(repository, new FixedClock());

        await Assert.ThrowsAsync<HouseholdMemberNotFoundException>(() =>
            service.GetAsync(Guid.NewGuid(), "day", Today, null, CancellationToken.None));
    }

    [Fact]
    public async Task Empty_household_reports_calendar_unavailable()
    {
        var repository = new FakeRepository([]);
        var service = new CalendarService(repository, new FixedClock());

        await Assert.ThrowsAsync<TodayBoardNotAvailableException>(() =>
            service.GetAsync(Adult.Id, "day", Today, null, CancellationToken.None));
    }

    private sealed class FixedClock : IHouseholdClock
    {
        public DateOnly Today => CalendarServiceTests.Today;

        public DateTimeOffset UtcNow => new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeRepository : ITodayBoardRepository
    {
        private readonly IReadOnlyList<HouseholdMember> _members;

        public FakeRepository(IReadOnlyList<HouseholdMember> members)
        {
            _members = members;
        }

        public List<Job> Jobs { get; } = [];

        public DateOnly? LastGenerationHorizon { get; private set; }

        public Task<IReadOnlyList<HouseholdMember>> GetMembersAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_members);

        public Task<HouseholdMember?> GetMemberAsync(Guid memberId, CancellationToken cancellationToken) =>
            Task.FromResult(_members.SingleOrDefault(member => member.Id == memberId));

        public Task<IReadOnlyList<Job>> GetJobsAsync(
            IReadOnlyCollection<Guid> childIds,
            DateOnly scheduledDate,
            CancellationToken cancellationToken) => throw Unused();

        public Task<Job?> GetJobAsync(Guid jobId, CancellationToken cancellationToken) => throw Unused();

        public Task<IReadOnlyList<TodayJobRejection>> GetLatestRejectionsAsync(
            IReadOnlyCollection<Guid> childIds,
            DateOnly scheduledDate,
            CancellationToken cancellationToken) => throw Unused();

        public Task<IReadOnlyList<Job>> GetJobsInRangeAsync(
            IReadOnlyCollection<Guid> childIds,
            DateOnly startDate,
            DateOnly endDate,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Job>>(Jobs
                .Where(job => childIds.Contains(job.ChildId)
                    && job.ScheduledDate >= startDate
                    && job.ScheduledDate <= endDate)
                .ToArray());

        public Task<IReadOnlyList<TodayJobRejection>> GetLatestRejectionsInRangeAsync(
            IReadOnlyCollection<Guid> childIds,
            DateOnly startDate,
            DateOnly endDate,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TodayJobRejection>>([]);

        public Task AddJobsAsync(IReadOnlyCollection<Job> jobs, CancellationToken cancellationToken) =>
            throw Unused();

        public Task<IReadOnlyList<RecurringJobSeries>> GetRecurringJobSeriesByRequestAsync(
            Guid assignmentRequestId,
            CancellationToken cancellationToken) => throw Unused();

        public Task<IReadOnlyList<RecurringJobSeries>> GetRecurringJobSeriesNeedingGenerationAsync(
            DateOnly horizon,
            CancellationToken cancellationToken)
        {
            LastGenerationHorizon = horizon;
            return Task.FromResult<IReadOnlyList<RecurringJobSeries>>([]);
        }

        public Task AddRecurringJobSeriesAsync(
            IReadOnlyCollection<RecurringJobSeries> series,
            CancellationToken cancellationToken) => throw Unused();

        public Task<int> GetRecurringJobSeriesOccurrenceCountAsync(
            Guid seriesId,
            CancellationToken cancellationToken) => throw Unused();

        public Task<TodayPointsSummary> GetPointsSummaryAsync(
            Guid childId,
            CancellationToken cancellationToken) => throw Unused();

        public Task AddPointsAwardAsync(PointsLedgerEntry entry, CancellationToken cancellationToken) =>
            throw Unused();

        public Task AddReviewDecisionAsync(
            JobReviewDecision decision,
            CancellationToken cancellationToken) => throw Unused();

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private static NotSupportedException Unused() => new("This operation is not used by the test.");
    }
}
