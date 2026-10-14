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
    public async Task Board_has_no_whose_turn_cards_before_any_configuration()
    {
        var board = await GetTodayAsync(DemoDataIds.Fredster);

        Assert.Empty(board.WhoseTurns);
    }

    [Fact]
    public async Task Adult_configures_a_rotation_and_the_board_shows_todays_child()
    {
        var overview = await CreateAsync(
            [DemoDataIds.Fredster, DemoDataIds.Harrie], DemoDataIds.Fredster, _today, "Who is Pink today?");

        var board = await GetTodayAsync(DemoDataIds.Fredster);

        var turn = Assert.Single(board.WhoseTurns);
        Assert.Equal(overview.Rotations[0].RotationId, turn.RotationId);
        Assert.Equal("Who is Pink today?", turn.Question);
        Assert.Equal(DemoDataIds.Fredster, turn.ChildId);
        Assert.Equal("Fredster", turn.ChildDisplayName);
    }

    [Fact]
    public async Task Several_rotations_show_side_by_side_and_advance_independently()
    {
        await CreateAsync([DemoDataIds.Fredster, DemoDataIds.Harrie], DemoDataIds.Fredster, _today, "Who is Pink today?");
        await CreateAsync([DemoDataIds.Fredster, DemoDataIds.Harrie], DemoDataIds.Harrie, _today, "Who sits next to Mum?");

        var day0 = await GetTodayAsync(DemoDataIds.Fredster, _today);
        var day1 = await GetTodayAsync(DemoDataIds.Fredster, _today.AddDays(1));

        Assert.Equal(
            [("Who is Pink today?", DemoDataIds.Fredster), ("Who sits next to Mum?", DemoDataIds.Harrie)],
            day0.WhoseTurns.Select(turn => (turn.Question, turn.ChildId)));
        Assert.Equal(
            [DemoDataIds.Harrie, DemoDataIds.Fredster],
            day1.WhoseTurns.Select(turn => turn.ChildId));
    }

    [Fact]
    public async Task One_child_rotation_always_answers_with_that_child()
    {
        await CreateAsync([DemoDataIds.Harrie], DemoDataIds.Harrie, _today, null);

        foreach (var offset in new[] { 0, 1, 5, 30 })
        {
            var board = await GetTodayAsync(DemoDataIds.Harrie, _today.AddDays(offset));
            Assert.Equal(DemoDataIds.Harrie, Assert.Single(board.WhoseTurns).ChildId);
        }
    }

    [Fact]
    public async Task Scheduling_a_revision_for_tomorrow_leaves_today_unchanged()
    {
        var created = await CreateAsync(
            [DemoDataIds.Fredster, DemoDataIds.Harrie], DemoDataIds.Fredster, _today, null);
        var rotationId = created.Rotations[0].RotationId;

        using var rescheduled = await SendAsync(
            HttpMethod.Put,
            $"/api/turn-rotations/{rotationId}",
            DemoDataIds.Addie,
            Body([DemoDataIds.Harrie], DemoDataIds.Harrie, _today.AddDays(1), "Who feeds the fish?"));
        Assert.Equal(HttpStatusCode.OK, rescheduled.StatusCode);

        var today = Assert.Single((await GetTodayAsync(DemoDataIds.Fredster, _today)).WhoseTurns);
        var tomorrow = Assert.Single((await GetTodayAsync(DemoDataIds.Fredster, _today.AddDays(1))).WhoseTurns);
        Assert.Equal(DemoDataIds.Fredster, today.ChildId);
        Assert.Equal("Who is Pink today?", today.Question);
        Assert.Equal(DemoDataIds.Harrie, tomorrow.ChildId);
        Assert.Equal("Who feeds the fish?", tomorrow.Question);
        Assert.Equal(rotationId, tomorrow.RotationId);
    }

    [Fact]
    public async Task Ending_one_rotation_leaves_the_others_running()
    {
        var first = await CreateAsync([DemoDataIds.Fredster], DemoDataIds.Fredster, _today, "Who is Pink today?");
        await CreateAsync([DemoDataIds.Harrie], DemoDataIds.Harrie, _today, "Who sits next to Mum?");

        using var end = await SendAsync(
            HttpMethod.Delete, $"/api/turn-rotations/{first.Rotations[0].RotationId}", DemoDataIds.Addie);
        Assert.Equal(HttpStatusCode.OK, end.StatusCode);

        Assert.Equal(2, (await GetTodayAsync(DemoDataIds.Fredster, _today)).WhoseTurns.Count);
        var tomorrow = Assert.Single((await GetTodayAsync(DemoDataIds.Fredster, _today.AddDays(1))).WhoseTurns);
        Assert.Equal("Who sits next to Mum?", tomorrow.Question);
        Assert.Equal(2, (await GetOverviewAsync(DemoDataIds.Addie)).Rotations.Count);
        using var again = await SendAsync(
            HttpMethod.Delete, $"/api/turn-rotations/{first.Rotations[0].RotationId}", DemoDataIds.Addie);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Overview_reports_each_rotation_with_its_configuration_and_preview()
    {
        await CreateAsync([DemoDataIds.Fredster, DemoDataIds.Harrie], DemoDataIds.Harrie, _today, null);

        var rotation = Assert.Single((await GetOverviewAsync(DemoDataIds.Addie)).Rotations);

        Assert.NotNull(rotation.Current);
        Assert.Equal(
            new[] { DemoDataIds.Fredster, DemoDataIds.Harrie }.Order(),
            rotation.Current!.ParticipantChildIds.Order());
        Assert.Equal(DemoDataIds.Harrie, rotation.Current.FirstChildId);
        Assert.Equal(DemoDataIds.Harrie, rotation.UpcomingTurns[0].ChildId);
    }

    [Fact]
    public async Task Deactivating_a_participant_excludes_them_from_tomorrow_and_restoring_does_not_re_add_them()
    {
        await CreateAsync([DemoDataIds.Fredster, DemoDataIds.Harrie], DemoDataIds.Fredster, _today, null);
        await CreateAsync([DemoDataIds.Fredster, DemoDataIds.Harrie], DemoDataIds.Harrie, _today, "Who sits next to Mum?");

        using var deactivate = await SendAsync(
            HttpMethod.Delete, $"/api/users/{DemoDataIds.Fredster}", DemoDataIds.Addie);
        Assert.True(deactivate.IsSuccessStatusCode, await deactivate.Content.ReadAsStringAsync());

        var todayBoard = await GetTodayAsync(DemoDataIds.Harrie, _today);
        Assert.Equal("Fredster", todayBoard.WhoseTurns[0].ChildDisplayName);
        foreach (var offset in new[] { 1, 2, 3 })
        {
            var board = await GetTodayAsync(DemoDataIds.Harrie, _today.AddDays(offset));
            Assert.Equal(2, board.WhoseTurns.Count);
            Assert.All(board.WhoseTurns, turn => Assert.Equal(DemoDataIds.Harrie, turn.ChildId));
        }

        using var restore = await SendAsync(
            HttpMethod.Post, $"/api/users/{DemoDataIds.Fredster}/restore", DemoDataIds.Addie);
        Assert.True(restore.IsSuccessStatusCode, await restore.Content.ReadAsStringAsync());
        var afterRestore = await GetTodayAsync(DemoDataIds.Harrie, _today.AddDays(2));
        Assert.All(afterRestore.WhoseTurns, turn => Assert.Equal(DemoDataIds.Harrie, turn.ChildId));
    }

    [Fact]
    public async Task Children_cannot_read_or_configure_rotations()
    {
        var created = await CreateAsync([DemoDataIds.Fredster], DemoDataIds.Fredster, _today, null);
        var id = created.Rotations[0].RotationId;
        var body = Body([DemoDataIds.Fredster], DemoDataIds.Fredster, _today.AddDays(1), null);

        using var read = await SendAsync(HttpMethod.Get, "/api/turn-rotations", DemoDataIds.Fredster);
        using var create = await SendAsync(HttpMethod.Post, "/api/turn-rotations", DemoDataIds.Fredster, body);
        using var update = await SendAsync(HttpMethod.Put, $"/api/turn-rotations/{id}", DemoDataIds.Fredster, body);
        using var end = await SendAsync(HttpMethod.Delete, $"/api/turn-rotations/{id}", DemoDataIds.Fredster);

        Assert.All(
            new[] { read, create, update, end },
            response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
    }

    [Fact]
    public async Task Invalid_configurations_are_rejected_with_field_errors()
    {
        using var noParticipants = await SendCreateAsync([], DemoDataIds.Fredster, _today);
        using var duplicate = await SendCreateAsync(
            [DemoDataIds.Fredster, DemoDataIds.Fredster], DemoDataIds.Fredster, _today);
        using var firstChildNotSelected = await SendCreateAsync(
            [DemoDataIds.Fredster, DemoDataIds.Harrie], DemoDataIds.Addie, _today);
        using var inactiveOrNonChild = await SendCreateAsync(
            [DemoDataIds.Fredster, DemoDataIds.Addie], DemoDataIds.Fredster, _today);
        using var unknownRotation = await SendAsync(
            HttpMethod.Put,
            $"/api/turn-rotations/{Guid.NewGuid()}",
            DemoDataIds.Addie,
            Body([DemoDataIds.Fredster], DemoDataIds.Fredster, _today.AddDays(1), null));

        Assert.All(
            new[] { noParticipants, duplicate, firstChildNotSelected, inactiveOrNonChild },
            response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        Assert.Equal(HttpStatusCode.NotFound, unknownRotation.StatusCode);
    }

    [Fact]
    public async Task A_change_to_an_existing_rotation_must_start_tomorrow_or_later()
    {
        var created = await CreateAsync([DemoDataIds.Fredster], DemoDataIds.Fredster, _today, null);
        var url = $"/api/turn-rotations/{created.Rotations[0].RotationId}";

        using var sameDay = await SendAsync(
            HttpMethod.Put, url, DemoDataIds.Addie, Body([DemoDataIds.Harrie], DemoDataIds.Harrie, _today, null));
        using var tomorrow = await SendAsync(
            HttpMethod.Put, url, DemoDataIds.Addie,
            Body([DemoDataIds.Harrie], DemoDataIds.Harrie, _today.AddDays(1), null));

        Assert.Equal(HttpStatusCode.BadRequest, sameDay.StatusCode);
        Assert.Equal(HttpStatusCode.OK, tomorrow.StatusCode);
    }

    [Fact]
    public async Task Rotations_default_to_daily_and_can_be_saved_to_move_on_when_ticked()
    {
        var daily = await CreateAsync([DemoDataIds.Fredster, DemoDataIds.Harrie], DemoDataIds.Fredster, _today, null);
        var dailyId = daily.Rotations[0].RotationId;
        Assert.Equal("daily", daily.Rotations[0].Current!.AdvanceMode);
        var dailyTurn = Assert.Single((await GetTodayAsync(DemoDataIds.Addie)).WhoseTurns);
        Assert.Equal(("daily", false, false), (dailyTurn.AdvanceMode, dailyTurn.TurnTaken, dailyTurn.CanTakeTurn));

        var ticked = await CreateAsync(
            [DemoDataIds.Fredster, DemoDataIds.Harrie], DemoDataIds.Harrie, _today, "Who is first in the shower?", "whenTicked");

        var summary = ticked.Rotations.Single(rotation => rotation.RotationId != dailyId);
        Assert.Equal("whenTicked", summary.Current!.AdvanceMode);
        Assert.Equal([DemoDataIds.Harrie, DemoDataIds.Fredster], summary.UpNext.Select(child => child.ChildId));
        Assert.Equal(DemoDataIds.Harrie, Assert.Single(summary.UpcomingTurns).ChildId);
        using var unknownMode = await SendAsync(
            HttpMethod.Post,
            "/api/turn-rotations",
            DemoDataIds.Addie,
            Body([DemoDataIds.Fredster], DemoDataIds.Fredster, _today, null, "weekly"));
        Assert.Equal(HttpStatusCode.BadRequest, unknownMode.StatusCode);
    }

    [Fact]
    public async Task Rotations_saved_before_the_migration_keep_moving_on_daily()
    {
        var rotationId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO turn_rotation_revisions
                    (id, rotation_id, effective_from, question, first_child_id, created_by_member_id, created_at_utc)
                VALUES
                    ({revisionId}, {rotationId}, {_today}, {"Who is Pink today?"}, {DemoDataIds.Harrie},
                     {DemoDataIds.Addie}, {DateTimeOffset.UtcNow});
                INSERT INTO turn_rotation_participants (turn_rotation_revision_id, order_index, child_id)
                VALUES ({revisionId}, 0, {DemoDataIds.Fredster}), ({revisionId}, 1, {DemoDataIds.Harrie});
                """);
        }

        var rotation = Assert.Single((await GetOverviewAsync(DemoDataIds.Addie)).Rotations);
        var tomorrow = Assert.Single((await GetTodayAsync(DemoDataIds.Addie, _today.AddDays(1))).WhoseTurns);

        Assert.Equal("daily", rotation.Current!.AdvanceMode);
        Assert.Equal(14, rotation.UpcomingTurns.Count);
        Assert.Equal(("daily", DemoDataIds.Fredster), (tomorrow.AdvanceMode, tomorrow.ChildId));
    }

    [Fact]
    public async Task Three_child_ticked_rotation_marks_today_done_and_moves_on_from_tomorrow()
    {
        var ivy = await CreateChildAsync("Ivy");
        var created = await CreateAsync(
            [DemoDataIds.Fredster, DemoDataIds.Harrie, ivy], DemoDataIds.Fredster, _today, "Who is first in the shower?", "whenTicked");
        var rotationId = created.Rotations[0].RotationId;
        var before = Assert.Single((await GetTodayAsync(DemoDataIds.Fredster)).WhoseTurns);
        Assert.Equal((DemoDataIds.Fredster, false, true), (before.ChildId, before.TurnTaken, before.CanTakeTurn));
        Assert.False(Assert.Single((await GetTodayAsync(DemoDataIds.Harrie)).WhoseTurns).CanTakeTurn);

        var taken = await TickAsync(rotationId, DemoDataIds.Fredster, DemoDataIds.Fredster);

        Assert.Equal((DemoDataIds.Fredster, DemoDataIds.Fredster, _today), (taken.ChildId, taken.RecordedByMemberId, taken.Date));
        var after = Assert.Single((await GetTodayAsync(DemoDataIds.Fredster)).WhoseTurns);
        Assert.Equal((DemoDataIds.Fredster, true, false), (after.ChildId, after.TurnTaken, after.CanTakeTurn));
        Assert.Equal(taken.Id, after.UndoableTurn!.TurnId);
        foreach (var offset in new[] { 1, 3 })
        {
            var later = Assert.Single((await GetTodayAsync(DemoDataIds.Harrie, _today.AddDays(offset))).WhoseTurns);
            Assert.Equal((DemoDataIds.Harrie, false, false), (later.ChildId, later.TurnTaken, later.CanTakeTurn));
        }

        using var again = await SendTickAsync(rotationId, DemoDataIds.Addie, DemoDataIds.Fredster);
        using var next = await SendTickAsync(rotationId, DemoDataIds.Harrie, DemoDataIds.Harrie);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, next.StatusCode);
    }

    [Fact]
    public async Task Two_child_ticked_rotation_moves_on_immediately_and_the_latest_tick_can_be_unticked()
    {
        var created = await CreateAsync(
            [DemoDataIds.Fredster, DemoDataIds.Harrie], DemoDataIds.Fredster, _today, "Who is first in the shower?", "whenTicked");
        var rotationId = created.Rotations[0].RotationId;

        var first = await TickAsync(rotationId, DemoDataIds.Fredster, DemoDataIds.Fredster);

        var forHarrie = Assert.Single((await GetTodayAsync(DemoDataIds.Harrie)).WhoseTurns);
        var forFredster = Assert.Single((await GetTodayAsync(DemoDataIds.Fredster)).WhoseTurns);
        Assert.Equal((DemoDataIds.Harrie, true), (forHarrie.ChildId, forHarrie.CanTakeTurn));
        Assert.Null(forHarrie.UndoableTurn);
        Assert.Equal((DemoDataIds.Harrie, false), (forFredster.ChildId, forFredster.CanTakeTurn));
        Assert.Equal(first.Id, forFredster.UndoableTurn!.TurnId);

        var second = await TickAsync(rotationId, DemoDataIds.Harrie, DemoDataIds.Harrie);
        Assert.Equal(DemoDataIds.Fredster, Assert.Single((await GetTodayAsync(DemoDataIds.Fredster)).WhoseTurns).ChildId);

        using var older = await SendAsync(
            HttpMethod.Delete, $"/api/turn-rotations/{rotationId}/turns/{first.Id}", DemoDataIds.Addie);
        Assert.Equal(HttpStatusCode.Conflict, older.StatusCode);
        using var undo = await SendAsync(
            HttpMethod.Delete, $"/api/turn-rotations/{rotationId}/turns/{second.Id}", DemoDataIds.Harrie);
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
        var undone = (await undo.Content.ReadFromJsonAsync<TakenTurnResponse>())!;
        Assert.Equal(DemoDataIds.Harrie, undone.UndoneByMemberId);
        Assert.NotNull(undone.UndoneAtUtc);
        Assert.Equal(DemoDataIds.Harrie, Assert.Single((await GetTodayAsync(DemoDataIds.Harrie)).WhoseTurns).ChildId);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await database.TakenTurns.AsNoTracking().OrderBy(turn => turn.RecordedAtUtc).ToListAsync();
        Assert.Equal([first.Id, second.Id], rows.Select(turn => turn.Id));
        Assert.Equal([null, DemoDataIds.Harrie], rows.Select(turn => turn.UndoneByMemberId));
    }

    [Fact]
    public async Task Only_adults_and_the_child_whose_turn_it_is_can_tick_or_untick()
    {
        var created = await CreateAsync(
            [DemoDataIds.Fredster, DemoDataIds.Harrie], DemoDataIds.Fredster, _today, null, "whenTicked");
        var rotationId = created.Rotations[0].RotationId;
        var daily = await CreateAsync([DemoDataIds.Harrie], DemoDataIds.Harrie, _today, "Who feeds the fish?");
        var dailyId = daily.Rotations.Single(rotation => rotation.RotationId != rotationId).RotationId;

        using var someoneElses = await SendTickAsync(rotationId, DemoDataIds.Harrie, DemoDataIds.Fredster);
        using var notYourTurn = await SendTickAsync(rotationId, DemoDataIds.Harrie, DemoDataIds.Harrie);
        using var dailyTick = await SendTickAsync(dailyId, DemoDataIds.Addie, DemoDataIds.Harrie);
        using var unknown = await SendTickAsync(Guid.NewGuid(), DemoDataIds.Addie, DemoDataIds.Fredster);
        Assert.Equal(HttpStatusCode.Forbidden, someoneElses.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, notYourTurn.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, dailyTick.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var taken = await TickAsync(rotationId, DemoDataIds.Addie, DemoDataIds.Fredster);
        using var harrieUndo = await SendAsync(
            HttpMethod.Delete, $"/api/turn-rotations/{rotationId}/turns/{taken.Id}", DemoDataIds.Harrie);
        using var unknownUndo = await SendAsync(
            HttpMethod.Delete, $"/api/turn-rotations/{rotationId}/turns/{Guid.NewGuid()}", DemoDataIds.Addie);
        Assert.Equal(HttpStatusCode.Forbidden, harrieUndo.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownUndo.StatusCode);
        Assert.Equal(DemoDataIds.Harrie, Assert.Single((await GetTodayAsync(DemoDataIds.Harrie)).WhoseTurns, turn => turn.RotationId == rotationId).ChildId);
    }

    [Fact]
    public async Task Simultaneous_ticks_for_the_same_turn_move_the_rotation_on_only_once()
    {
        var created = await CreateAsync(
            [DemoDataIds.Fredster, DemoDataIds.Harrie], DemoDataIds.Fredster, _today, null, "whenTicked");
        var rotationId = created.Rotations[0].RotationId;

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => SendTickAsync(rotationId, DemoDataIds.Addie, DemoDataIds.Fredster)));

        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Equal(3, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(DemoDataIds.Harrie, Assert.Single((await GetTodayAsync(DemoDataIds.Addie)).WhoseTurns).ChildId);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
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
        using var response = await SendAsync(HttpMethod.Get, "/api/turn-rotations", memberId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OverviewResponse>())!;
    }

    private async Task<OverviewResponse> CreateAsync(
        Guid[] participantChildIds,
        Guid firstChildId,
        DateOnly effectiveFrom,
        string? question,
        string? advanceMode = null)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "/api/turn-rotations",
            DemoDataIds.Addie,
            Body(participantChildIds, firstChildId, effectiveFrom, question, advanceMode));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OverviewResponse>())!;
    }

    private async Task<Guid> CreateChildAsync(string firstName)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "/api/users",
            DemoDataIds.Addie,
            new { firstName, surname = "Avenant", nickname = (string?)null, role = "child" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedMemberResponse>())!.Id;
    }

    private async Task<TakenTurnResponse> TickAsync(Guid rotationId, Guid memberId, Guid childId)
    {
        using var response = await SendTickAsync(rotationId, memberId, childId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TakenTurnResponse>())!;
    }

    private Task<HttpResponseMessage> SendTickAsync(Guid rotationId, Guid memberId, Guid childId) =>
        SendAsync(HttpMethod.Post, $"/api/turn-rotations/{rotationId}/turns", memberId, new { childId });

    private Task<HttpResponseMessage> SendCreateAsync(
        Guid[] participantChildIds,
        Guid firstChildId,
        DateOnly effectiveFrom) =>
        SendAsync(
            HttpMethod.Post,
            "/api/turn-rotations",
            DemoDataIds.Addie,
            Body(participantChildIds, firstChildId, effectiveFrom, null));

    private static object Body(
        Guid[] participantChildIds,
        Guid firstChildId,
        DateOnly effectiveFrom,
        string? question,
        string? advanceMode = null) =>
        new { participantChildIds, firstChildId, effectiveFrom, question, advanceMode };

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

    private sealed record WhoseTurnResponse(
        Guid RotationId,
        string Question,
        Guid ChildId,
        string ChildDisplayName,
        string AdvanceMode,
        bool TurnTaken,
        bool CanTakeTurn,
        UndoableTurnResponse? UndoableTurn);

    private sealed record UndoableTurnResponse(Guid TurnId, Guid ChildId, string ChildDisplayName);

    private sealed record BoardResponse(IReadOnlyList<WhoseTurnResponse> WhoseTurns);

    private sealed record ConfigurationResponse(
        Guid Id,
        DateOnly EffectiveFrom,
        string Question,
        IReadOnlyList<Guid> ParticipantChildIds,
        Guid FirstChildId,
        string AdvanceMode);

    private sealed record TurnResponse(DateOnly Date, string Question, Guid ChildId);

    private sealed record ChildResponse(Guid ChildId, string ChildDisplayName);

    private sealed record SummaryResponse(
        Guid RotationId,
        ConfigurationResponse? Current,
        IReadOnlyList<TurnResponse> UpcomingTurns,
        IReadOnlyList<ChildResponse> UpNext);

    private sealed record TakenTurnResponse(
        Guid Id,
        Guid RotationId,
        DateOnly Date,
        Guid ChildId,
        string ChildDisplayName,
        Guid RecordedByMemberId,
        DateTimeOffset RecordedAtUtc,
        Guid? UndoneByMemberId,
        DateTimeOffset? UndoneAtUtc);

    private sealed record CreatedMemberResponse(Guid Id);

    private sealed record OverviewResponse(IReadOnlyList<SummaryResponse> Rotations);
}
