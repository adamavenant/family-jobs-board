using System.Net;
using System.Net.Http.Json;
using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace FamilyJobsBoard.Api.IntegrationTests;

public sealed class TurnRotationEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("family_jobs_board_tests")
        .WithUsername("family_jobs_board")
        .WithPassword("family_jobs_board")
        .Build();

    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private DateOnly _today;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new TodayEndpointsTests.TestApiFactory(_postgres.GetConnectionString());
        _client = _factory.CreateClient();

        await using var scope = _factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await database.Database.MigrateAsync();
        var clock = scope.ServiceProvider.GetRequiredService<IHouseholdClock>();
        _today = clock.Today;
        await new DemoDataSeeder(database).SeedAsync(_today, CancellationToken.None);
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
    public async Task Board_has_no_whose_turn_card_before_any_configuration()
    {
        var board = await GetTodayAsync(DemoDataIds.Fredster);

        Assert.Null(board.WhoseTurn);
    }

    [Fact]
    public async Task Adult_configures_a_rotation_and_the_board_shows_todays_child()
    {
        using var save = await SaveAsync(
            [DemoDataIds.Fredster, DemoDataIds.Harrie], DemoDataIds.Fredster, _today, "Who is Pink today?");
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var board = await GetTodayAsync(DemoDataIds.Fredster);

        Assert.NotNull(board.WhoseTurn);
        Assert.Equal("Who is Pink today?", board.WhoseTurn!.Question);
        Assert.Equal(DemoDataIds.Fredster, board.WhoseTurn.ChildId);
        Assert.Equal("Fredster", board.WhoseTurn.ChildDisplayName);
    }

    [Fact]
    public async Task Rotation_alternates_between_two_children_on_consecutive_days()
    {
        await SaveAsync([DemoDataIds.Fredster, DemoDataIds.Harrie], DemoDataIds.Fredster, _today, null);

        var day0 = await GetTodayAsync(DemoDataIds.Fredster, _today);
        var day1 = await GetTodayAsync(DemoDataIds.Fredster, _today.AddDays(1));
        var day2 = await GetTodayAsync(DemoDataIds.Fredster, _today.AddDays(2));

        Assert.Equal(DemoDataIds.Fredster, day0.WhoseTurn!.ChildId);
        Assert.Equal(DemoDataIds.Harrie, day1.WhoseTurn!.ChildId);
        Assert.Equal(DemoDataIds.Fredster, day2.WhoseTurn!.ChildId);
    }

    [Fact]
    public async Task One_child_rotation_always_answers_with_that_child()
    {
        await SaveAsync([DemoDataIds.Harrie], DemoDataIds.Harrie, _today, null);

        foreach (var offset in new[] { 0, 1, 5, 30 })
        {
            var board = await GetTodayAsync(DemoDataIds.Harrie, _today.AddDays(offset));
            Assert.Equal(DemoDataIds.Harrie, board.WhoseTurn!.ChildId);
        }
    }

    [Fact]
    public async Task Scheduling_a_revision_for_tomorrow_leaves_today_unchanged()
    {
        await SaveAsync([DemoDataIds.Fredster, DemoDataIds.Harrie], DemoDataIds.Fredster, _today, null);
        var todayBefore = await GetTodayAsync(DemoDataIds.Fredster, _today);

        using var rescheduled = await SaveAsync(
            [DemoDataIds.Harrie], DemoDataIds.Harrie, _today.AddDays(1), "Who feeds the fish?");
        Assert.Equal(HttpStatusCode.OK, rescheduled.StatusCode);

        var todayAfter = await GetTodayAsync(DemoDataIds.Fredster, _today);
        var tomorrow = await GetTodayAsync(DemoDataIds.Fredster, _today.AddDays(1));

        Assert.Equal(todayBefore.WhoseTurn!.ChildId, todayAfter.WhoseTurn!.ChildId);
        Assert.Equal("Who is Pink today?", todayAfter.WhoseTurn.Question);
        Assert.Equal(DemoDataIds.Harrie, tomorrow.WhoseTurn!.ChildId);
        Assert.Equal("Who feeds the fish?", tomorrow.WhoseTurn.Question);
    }

    [Fact]
    public async Task Overview_reports_the_current_configuration_and_an_upcoming_preview()
    {
        await SaveAsync([DemoDataIds.Fredster, DemoDataIds.Harrie], DemoDataIds.Harrie, _today, null);

        var overview = await GetOverviewAsync(DemoDataIds.Addie);

        Assert.NotNull(overview.Current);
        Assert.Equal(
            new[] { DemoDataIds.Fredster, DemoDataIds.Harrie }.Order(),
            overview.Current!.ParticipantChildIds.Order());
        Assert.Equal(DemoDataIds.Harrie, overview.Current.FirstChildId);
        Assert.NotEmpty(overview.UpcomingTurns);
        Assert.Equal(DemoDataIds.Harrie, overview.UpcomingTurns[0].ChildId);
    }

    [Fact]
    public async Task Deactivating_a_participant_excludes_them_from_tomorrow_and_restoring_does_not_re_add_them()
    {
        await SaveAsync([DemoDataIds.Fredster, DemoDataIds.Harrie], DemoDataIds.Fredster, _today, null);

        using var deactivate = await SendAsync(
            HttpMethod.Delete, $"/api/users/{DemoDataIds.Fredster}", DemoDataIds.Addie);
        Assert.True(deactivate.IsSuccessStatusCode, await deactivate.Content.ReadAsStringAsync());

        var todayBoard = await GetTodayAsync(DemoDataIds.Harrie, _today);
        Assert.Equal(DemoDataIds.Fredster, todayBoard.WhoseTurn!.ChildId);
        Assert.Equal("Fredster", todayBoard.WhoseTurn.ChildDisplayName);
        foreach (var offset in new[] { 1, 2, 3 })
        {
            var board = await GetTodayAsync(DemoDataIds.Harrie, _today.AddDays(offset));
            Assert.Equal(DemoDataIds.Harrie, board.WhoseTurn!.ChildId);
        }

        using var restore = await SendAsync(
            HttpMethod.Post, $"/api/users/{DemoDataIds.Fredster}/restore", DemoDataIds.Addie);
        Assert.True(restore.IsSuccessStatusCode, await restore.Content.ReadAsStringAsync());
        var afterRestore = await GetTodayAsync(DemoDataIds.Harrie, _today.AddDays(2));
        Assert.Equal(DemoDataIds.Harrie, afterRestore.WhoseTurn!.ChildId);
    }

    [Fact]
    public async Task Children_cannot_read_or_configure_the_rotation()
    {
        using var read = await SendAsync(HttpMethod.Get, "/api/turn-rotation", DemoDataIds.Fredster);
        using var write = await SaveRawAsync(
            DemoDataIds.Fredster,
            [DemoDataIds.Fredster],
            DemoDataIds.Fredster,
            _today,
            null);

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Invalid_configurations_are_rejected_with_field_errors()
    {
        using var noParticipants = await SaveAsync([], DemoDataIds.Fredster, _today, null);
        using var duplicate = await SaveAsync(
            [DemoDataIds.Fredster, DemoDataIds.Fredster], DemoDataIds.Fredster, _today, null);
        using var firstChildNotSelected = await SaveAsync(
            [DemoDataIds.Fredster, DemoDataIds.Harrie], DemoDataIds.Addie, _today, null);
        using var inactiveOrNonChild = await SaveAsync(
            [DemoDataIds.Fredster, DemoDataIds.Addie], DemoDataIds.Fredster, _today, null);

        Assert.Equal(HttpStatusCode.BadRequest, noParticipants.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, firstChildNotSelected.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inactiveOrNonChild.StatusCode);
    }

    [Fact]
    public async Task A_second_configuration_on_the_same_day_must_start_tomorrow_or_later()
    {
        await SaveAsync([DemoDataIds.Fredster], DemoDataIds.Fredster, _today, null);

        using var sameDay = await SaveAsync([DemoDataIds.Harrie], DemoDataIds.Harrie, _today, null);
        using var tomorrow = await SaveAsync(
            [DemoDataIds.Harrie], DemoDataIds.Harrie, _today.AddDays(1), null);

        Assert.Equal(HttpStatusCode.BadRequest, sameDay.StatusCode);
        Assert.Equal(HttpStatusCode.OK, tomorrow.StatusCode);
    }

    private async Task<BoardResponse> GetTodayAsync(Guid memberId, DateOnly? date = null)
    {
        var url = date is null ? "/api/today" : $"/api/today?date={date:yyyy-MM-dd}";
        using var response = await SendAsync(HttpMethod.Get, url, memberId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BoardResponse>())!;
    }

    private async Task<OverviewResponse> GetOverviewAsync(Guid memberId)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/turn-rotation", memberId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OverviewResponse>())!;
    }

    private Task<HttpResponseMessage> SaveAsync(
        Guid[] participantChildIds,
        Guid firstChildId,
        DateOnly effectiveFrom,
        string? question) =>
        SaveRawAsync(DemoDataIds.Addie, participantChildIds, firstChildId, effectiveFrom, question);

    private Task<HttpResponseMessage> SaveRawAsync(
        Guid actorMemberId,
        Guid[] participantChildIds,
        Guid firstChildId,
        DateOnly effectiveFrom,
        string? question) =>
        SendAsync(
            HttpMethod.Put,
            "/api/turn-rotation",
            actorMemberId,
            new { participantChildIds, firstChildId, effectiveFrom, question });

    private Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        Guid memberId,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-Member-Id", memberId.ToString());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _client!.SendAsync(request);
    }

    private sealed record WhoseTurnResponse(string Question, Guid ChildId, string ChildDisplayName);

    private sealed record BoardResponse(WhoseTurnResponse? WhoseTurn);

    private sealed record ConfigurationResponse(
        Guid Id,
        DateOnly EffectiveFrom,
        string Question,
        IReadOnlyList<Guid> ParticipantChildIds,
        Guid FirstChildId);

    private sealed record TurnResponse(DateOnly Date, string Question, Guid ChildId);

    private sealed record OverviewResponse(
        ConfigurationResponse? Current,
        IReadOnlyList<TurnResponse> UpcomingTurns);
}
