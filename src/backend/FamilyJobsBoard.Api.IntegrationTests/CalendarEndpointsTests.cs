using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace FamilyJobsBoard.Api.IntegrationTests;

public sealed class CalendarEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("family_jobs_board_tests")
        .WithUsername("family_jobs_board")
        .WithPassword("family_jobs_board")
        .Build();

    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new TodayEndpointsTests.TestApiFactory(_postgres.GetConnectionString());
        _client = _factory.CreateClient();

        await using var scope = _factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await database.Database.MigrateAsync();
        var clock = scope.ServiceProvider.GetRequiredService<IHouseholdClock>();
        await new DemoDataSeeder(database).SeedAsync(clock.Today, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Default_calendar_is_the_week_containing_today()
    {
        var board = await GetCalendarAsync();

        Assert.Equal("week", board.View);
        Assert.Equal(CurrentDate, board.CurrentDate);
        Assert.Equal(7, board.Days.Count);
        Assert.InRange(CurrentDate, board.RangeStart, board.RangeEnd);
        Assert.Equal(DayOfWeek.Monday, board.RangeStart.DayOfWeek);
    }

    [Fact]
    public async Task Day_view_shows_exactly_the_same_jobs_as_the_daily_agenda_for_that_date()
    {
        var agenda = await GetTodayAsync(CurrentDate);
        var calendar = await GetCalendarAsync(view: "day", date: CurrentDate);

        var day = Assert.Single(calendar.Days);
        Assert.Equal(CurrentDate, day.Date);
        AssertSameJobs(agenda.Jobs, day.Jobs);
    }

    [Fact]
    public async Task Week_view_matches_the_daily_agenda_for_every_day_in_the_week()
    {
        var calendar = await GetCalendarAsync(view: "week", date: CurrentDate);

        foreach (var day in calendar.Days)
        {
            var agenda = await GetTodayAsync(day.Date);
            AssertSameJobs(agenda.Jobs, day.Jobs);
        }
    }

    [Fact]
    public async Task Month_view_is_a_stable_forty_two_day_grid_with_adjacent_month_days_marked()
    {
        var board = await GetCalendarAsync(view: "month", date: CurrentDate);

        Assert.Equal(42, board.Days.Count);
        var focusedMonth = new DateOnly(CurrentDate.Year, CurrentDate.Month, 1);
        Assert.All(
            board.Days.Where(day => day.Date.Year == focusedMonth.Year && day.Date.Month == focusedMonth.Month),
            day => Assert.True(day.IsInFocusedPeriod));
        Assert.Contains(board.Days, day => !day.IsInFocusedPeriod);
    }

    [Fact]
    public async Task February_of_a_leap_year_still_produces_a_full_grid_covering_the_whole_month()
    {
        // Any five consecutive years contain a leap year, so this always finds one within the
        // ±2-year browsing horizon, however far in the future this suite is run.
        var leapYear = NearbyLeapYear(CurrentDate.Year);

        var board = await GetCalendarAsync(view: "month", date: new DateOnly(leapYear, 2, 14));

        Assert.Equal(42, board.Days.Count);
        Assert.True(board.RangeStart <= new DateOnly(leapYear, 2, 1));
        Assert.True(board.RangeEnd >= new DateOnly(leapYear, 2, 29));
    }

    [Fact]
    public async Task A_date_far_outside_the_browsing_horizon_is_rejected_without_generating_anything()
    {
        var before = await CountJobsAsync();

        using var future = await SendAsync(
            HttpMethod.Get, $"/api/calendar?view=day&date={CurrentDate.AddYears(3):yyyy-MM-dd}", DemoDataIds.Addie);
        using var past = await SendAsync(
            HttpMethod.Get, $"/api/calendar?view=day&date={CurrentDate.AddYears(-3):yyyy-MM-dd}", DemoDataIds.Addie);
        using var agendaFuture = await SendAsync(
            HttpMethod.Get, $"/api/today?date={CurrentDate.AddYears(3):yyyy-MM-dd}", DemoDataIds.Addie);

        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        Assert.Contains("Date", await ErrorFieldsAsync(future));
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, agendaFuture.StatusCode);
        Assert.Contains("Date", await ErrorFieldsAsync(agendaFuture));
        Assert.Equal(before, await CountJobsAsync());
    }

    private static int NearbyLeapYear(int aroundYear)
    {
        for (var offset = 0; offset <= 4; offset++)
        {
            if (DateTime.IsLeapYear(aroundYear + offset))
            {
                return aroundYear + offset;
            }

            if (DateTime.IsLeapYear(aroundYear - offset))
            {
                return aroundYear - offset;
            }
        }

        throw new InvalidOperationException("No leap year found near the given year.");
    }

    private async Task<int> CountJobsAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await database.Jobs.CountAsync();
    }

    [Fact]
    public async Task A_past_incomplete_job_still_appears_on_its_originally_assigned_date()
    {
        // Once-off job creation rejects a past date, but an occurrence can legitimately end up
        // in the past simply by never being completed while time moved on; seed that state
        // directly, the way it would exist for a genuinely overdue job.
        var pastDate = CurrentDate.AddDays(-10);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.Jobs.Add(new FamilyJobsBoard.Domain.Jobs.Job(
                Guid.NewGuid(), DemoDataIds.Harrie, "Overdue chore", "", 3, pastDate));
            await database.SaveChangesAsync();
        }

        var board = await GetCalendarAsync(view: "month", date: pastDate);

        var day = board.Days.Single(item => item.Date == pastDate);
        var job = Assert.Single(day.Jobs, item => item.Name == "Overdue chore");
        Assert.Equal("open", job.Status);
    }

    [Fact]
    public async Task A_cancelled_job_is_hidden_from_the_calendar_just_like_the_daily_agenda()
    {
        using var add = await SendAsync(
            HttpMethod.Post,
            "/api/today/jobs",
            DemoDataIds.Addie,
            new
            {
                childIds = new[] { DemoDataIds.Harrie },
                name = "No longer needed",
                description = "",
                points = 4,
                scheduledDate = CurrentDate,
                agendaPeriod = "unscheduled",
                scheduledTime = (string?)null,
            });
        add.EnsureSuccessStatusCode();
        var addedJobId = (await add.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("jobs")[0].GetProperty("id").GetGuid();
        using var cancel = await SendAsync(
            HttpMethod.Post,
            $"/api/jobs/{addedJobId}/cancel",
            DemoDataIds.Addie,
            new { reason = (string?)null });
        cancel.EnsureSuccessStatusCode();

        var dayView = await GetCalendarAsync(view: "day", date: CurrentDate);
        var weekView = await GetCalendarAsync(view: "week", date: CurrentDate);
        var agenda = await GetTodayAsync(CurrentDate);

        Assert.DoesNotContain(dayView.Days[0].Jobs, job => job.Id == addedJobId);
        Assert.DoesNotContain(
            weekView.Days.SelectMany(day => day.Jobs),
            job => job.Id == addedJobId);
        Assert.DoesNotContain(agenda.Jobs, job => job.Id == addedJobId);
    }

    [Fact]
    public async Task Recurring_occurrences_are_generated_across_the_whole_visible_range()
    {
        using var create = await SendAsync(
            HttpMethod.Post,
            "/api/recurring-jobs/daily",
            DemoDataIds.Addie,
            new
            {
                requestId = Guid.NewGuid(),
                childIds = new[] { DemoDataIds.Harrie },
                name = "Water the plants",
                description = "",
                points = 2,
                agendaPeriod = "morning",
                scheduledTime = (string?)null,
                startDate = CurrentDate,
                endDate = (DateOnly?)null,
            });
        create.EnsureSuccessStatusCode();
        var farAnchor = CurrentDate.AddDays(70);

        var board = await GetCalendarAsync(view: "week", date: farAnchor, childId: DemoDataIds.Harrie);

        Assert.All(board.Days, day => Assert.Contains(
            day.Jobs,
            job => job.Name == "Water the plants" && job.RecurrenceFrequency == "daily"));
    }

    [Fact]
    public async Task Child_filter_limits_the_calendar_to_that_child()
    {
        var board = await GetCalendarAsync(view: "day", date: CurrentDate, childId: DemoDataIds.Harrie);

        Assert.Equal(DemoDataIds.Harrie, board.SelectedChildId);
        Assert.All(
            board.Days.SelectMany(day => day.Jobs),
            job => Assert.Equal(DemoDataIds.Harrie, job.ChildId));
    }

    [Fact]
    public async Task Unknown_child_filter_is_rejected()
    {
        using var response = await SendAsync(
            HttpMethod.Get, $"/api/calendar?view=day&childId={Guid.NewGuid()}", DemoDataIds.Addie);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ChildId", await ErrorFieldsAsync(response));
    }

    [Fact]
    public async Task Invalid_view_is_rejected()
    {
        using var response = await SendAsync(
            HttpMethod.Get, "/api/calendar?view=fortnight", DemoDataIds.Addie);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("View", await ErrorFieldsAsync(response));
    }

    [Fact]
    public async Task Children_cannot_open_the_calendar()
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/calendar", DemoDataIds.Fredster);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private DateOnly CurrentDate => (_factory
        ?? throw new InvalidOperationException("Test API was not initialised."))
        .Services.GetRequiredService<IHouseholdClock>()
        .Today;

    private static void AssertSameJobs(
        IReadOnlyList<TodayEndpointsTests.JobResponse> agendaJobs,
        IReadOnlyList<CalendarJobResponse> calendarJobs)
    {
        Assert.Equal(agendaJobs.Count, calendarJobs.Count);
        Assert.Equal(
            agendaJobs.Select(job => job.Id).Order(),
            calendarJobs.Select(job => job.Id).Order());
        foreach (var agendaJob in agendaJobs)
        {
            var calendarJob = calendarJobs.Single(job => job.Id == agendaJob.Id);
            Assert.Equal(agendaJob.Name, calendarJob.Name);
            Assert.Equal(agendaJob.Status, calendarJob.Status);
            Assert.Equal(agendaJob.Points, calendarJob.Points);
            Assert.Equal(agendaJob.ChildId, calendarJob.ChildId);
            Assert.Equal(agendaJob.ScheduledDate, calendarJob.ScheduledDate);
        }
    }

    private async Task<TodayEndpointsTests.TodayResponse> GetTodayAsync(DateOnly date)
    {
        using var response = await SendAsync(
            HttpMethod.Get, $"/api/today?date={date:yyyy-MM-dd}", DemoDataIds.Addie);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TodayEndpointsTests.TodayResponse>())!;
    }

    private async Task<CalendarResponse> GetCalendarAsync(
        string? view = null,
        DateOnly? date = null,
        Guid? childId = null)
    {
        var query = new List<string>();
        if (view is not null)
        {
            query.Add($"view={view}");
        }

        if (date is not null)
        {
            query.Add($"date={date:yyyy-MM-dd}");
        }

        if (childId is not null)
        {
            query.Add($"childId={childId}");
        }

        var url = query.Count == 0 ? "/api/calendar" : $"/api/calendar?{string.Join('&', query)}";
        using var response = await SendAsync(HttpMethod.Get, url, DemoDataIds.Addie);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CalendarResponse>())!;
    }

    private static async Task<IReadOnlyCollection<string>> ErrorFieldsAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.GetProperty("errors").EnumerateObject().Select(item => item.Name).ToArray();
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, Guid memberId, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-Member-Id", memberId.ToString());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _client!.SendAsync(request);
    }

    private sealed record MemberResponse(Guid Id, string DisplayName, bool IsAdult);

    private sealed record CalendarJobResponse(
        Guid Id,
        Guid ChildId,
        string Name,
        int Points,
        DateOnly ScheduledDate,
        string? RecurrenceFrequency,
        string Status);

    private sealed record CalendarDayResponse(
        DateOnly Date,
        bool IsInFocusedPeriod,
        IReadOnlyList<CalendarJobResponse> Jobs);

    private sealed record CalendarResponse(
        MemberResponse Viewer,
        IReadOnlyList<MemberResponse> Members,
        string View,
        DateOnly AnchorDate,
        DateOnly CurrentDate,
        DateOnly RangeStart,
        DateOnly RangeEnd,
        Guid? SelectedChildId,
        IReadOnlyList<CalendarDayResponse> Days);
}
