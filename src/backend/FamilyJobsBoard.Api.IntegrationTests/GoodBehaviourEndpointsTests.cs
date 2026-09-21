using System.Net;
using System.Net.Http.Json;
using FamilyJobsBoard.Domain.GoodBehaviours;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace FamilyJobsBoard.Api.IntegrationTests;

public sealed class GoodBehaviourEndpointsTests : IAsyncLifetime
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
        await new DemoDataSeeder(database).SeedAsync(
            new DateOnly(2026, 9, 21),
            CancellationToken.None);
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
    public async Task Adult_created_type_is_available_to_log_and_children_can_read_it()
    {
        var created = await CreateTypeAsync("Tidying up", "Put things away.", 4);

        var listed = await GetTypesAsync(DemoDataIds.Fredster);
        var type = Assert.Single(listed.Types, item => item.Id == created.Id);
        Assert.Equal("Tidying up", type.Name);
        Assert.Equal(4, type.Points);

        using var log = await LogAsync(created.Id, DemoDataIds.Harrie, points: null);
        Assert.Equal(HttpStatusCode.Created, log.StatusCode);
    }

    [Fact]
    public async Task Logging_awards_points_immediately_and_appears_in_the_childs_history()
    {
        using var response = await LogAsync(DemoDataIds.BeingBrave, DemoDataIds.Fredster, points: 12);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var logged = await response.Content.ReadFromJsonAsync<LogResponse>();
        Assert.NotNull(logged);
        Assert.Equal(12, Assert.Single(logged.Awards).PointsBalance);

        var board = await GetTodayAsync(DemoDataIds.Fredster);
        Assert.Equal(12, board.PointsBalance);
        var earning = Assert.Single(board.PointEarnings);
        Assert.Equal("goodBehaviour", earning.Source);
        Assert.Equal("Being Brave", earning.Name);
        Assert.Equal(12, earning.Points);
        Assert.Equal("Addie", earning.LoggedByDisplayName);
        Assert.Null(earning.JobId);
    }

    [Fact]
    public async Task Behaviour_history_sits_alongside_job_earned_points()
    {
        await LogAsync(DemoDataIds.BeingHelpful, DemoDataIds.Fredster, points: null);
        using var complete = await SendAsync(
            HttpMethod.Post,
            $"/api/jobs/{DemoDataIds.FeedDog}/complete",
            DemoDataIds.Fredster);
        complete.EnsureSuccessStatusCode();
        using var approve = await SendAsync(
            HttpMethod.Post,
            $"/api/jobs/{DemoDataIds.FeedDog}/approve",
            DemoDataIds.Addie);
        approve.EnsureSuccessStatusCode();

        var board = await GetTodayAsync(DemoDataIds.Fredster);

        Assert.Equal(10, board.PointsBalance);
        Assert.Equal(
            ["job", "goodBehaviour"],
            board.PointEarnings.Select(entry => entry.Source));
    }

    [Fact]
    public async Task Retried_request_creates_one_ledger_entry()
    {
        var requestId = Guid.NewGuid();

        using var first = await LogAsync(DemoDataIds.BeingBrave, DemoDataIds.Harrie, null, requestId);
        using var retry = await LogAsync(DemoDataIds.BeingBrave, DemoDataIds.Harrie, null, requestId);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<LogResponse>();
        var retryBody = await retry.Content.ReadFromJsonAsync<LogResponse>();
        Assert.Equal(
            Assert.Single(firstBody!.Awards).Behaviour.Id,
            Assert.Single(retryBody!.Awards).Behaviour.Id);
        Assert.Equal(10, retryBody.Awards[0].PointsBalance);
        Assert.Equal(1, await CountLedgerEntriesAsync(DemoDataIds.Harrie));
    }

    [Fact]
    public async Task Concurrent_duplicate_requests_create_one_ledger_entry()
    {
        var requestId = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var response = await LogAsync(
                DemoDataIds.ShowingKindness,
                DemoDataIds.Harrie,
                null,
                requestId);
            return response.StatusCode;
        }));

        Assert.All(responses, status =>
            Assert.True(status is HttpStatusCode.Created or HttpStatusCode.OK, status.ToString()));
        Assert.Equal(1, await CountLedgerEntriesAsync(DemoDataIds.Harrie));
    }

    [Fact]
    public async Task Reusing_a_request_id_for_different_details_is_rejected()
    {
        var requestId = Guid.NewGuid();
        using var first = await LogAsync(DemoDataIds.BeingBrave, DemoDataIds.Harrie, 5, requestId);

        using var different = await LogAsync(DemoDataIds.BeingBrave, DemoDataIds.Fredster, 5, requestId);

        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        Assert.Equal(1, await CountLedgerEntriesAsync(DemoDataIds.Harrie));
        Assert.Equal(0, await CountLedgerEntriesAsync(DemoDataIds.Fredster));
    }

    [Fact]
    public async Task Logging_for_both_children_awards_each_child_independently()
    {
        using var response = await LogForAsync(
            DemoDataIds.BeingBrave,
            [DemoDataIds.Fredster, DemoDataIds.Harrie],
            points: 7);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var logged = await response.Content.ReadFromJsonAsync<LogResponse>();
        Assert.Equal(2, logged!.Awards.Count);
        Assert.All(logged.Awards, award => Assert.Equal(7, award.PointsBalance));
        Assert.Equal(
            new[] { DemoDataIds.Fredster, DemoDataIds.Harrie }.Order(),
            logged.Awards.Select(award => award.Behaviour.ChildId).Order());
        Assert.Equal(1, await CountLedgerEntriesAsync(DemoDataIds.Fredster));
        Assert.Equal(1, await CountLedgerEntriesAsync(DemoDataIds.Harrie));
        foreach (var child in new[] { DemoDataIds.Fredster, DemoDataIds.Harrie })
        {
            var board = await GetTodayAsync(child);
            Assert.Equal(7, board.PointsBalance);
            Assert.Equal("goodBehaviour", Assert.Single(board.PointEarnings).Source);
        }
    }

    [Fact]
    public async Task Retrying_a_two_child_request_awards_each_child_once()
    {
        var requestId = Guid.NewGuid();
        Guid[] children = [DemoDataIds.Fredster, DemoDataIds.Harrie];

        using var first = await LogForAsync(DemoDataIds.BeingHelpful, children, null, requestId);
        using var retry = await LogForAsync(
            DemoDataIds.BeingHelpful,
            [DemoDataIds.Harrie, DemoDataIds.Fredster],
            null,
            requestId);
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            using var response = await LogForAsync(
                DemoDataIds.BeingHelpful,
                children,
                null,
                requestId);
            return response.StatusCode;
        }));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.All(concurrent, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(1, await CountLedgerEntriesAsync(DemoDataIds.Fredster));
        Assert.Equal(1, await CountLedgerEntriesAsync(DemoDataIds.Harrie));
    }

    [Fact]
    public async Task Changing_the_children_for_a_used_request_id_is_rejected()
    {
        var requestId = Guid.NewGuid();
        using var first = await LogAsync(DemoDataIds.BeingBrave, DemoDataIds.Fredster, null, requestId);

        using var wider = await LogForAsync(
            DemoDataIds.BeingBrave,
            [DemoDataIds.Fredster, DemoDataIds.Harrie],
            null,
            requestId);

        Assert.Equal(HttpStatusCode.Conflict, wider.StatusCode);
        Assert.Equal(0, await CountLedgerEntriesAsync(DemoDataIds.Harrie));
    }

    [Fact]
    public async Task One_invalid_child_rejects_the_whole_request_and_awards_nobody()
    {
        using var withAdult = await LogForAsync(
            DemoDataIds.BeingBrave,
            [DemoDataIds.Fredster, DemoDataIds.Hellie],
            null);
        using var withUnknown = await LogForAsync(
            DemoDataIds.BeingBrave,
            [DemoDataIds.Fredster, Guid.NewGuid()],
            null);
        using var none = await LogForAsync(DemoDataIds.BeingBrave, [], null);
        using var duplicate = await LogForAsync(
            DemoDataIds.BeingBrave,
            [DemoDataIds.Fredster, DemoDataIds.Fredster],
            null);

        Assert.All(
            new[] { withAdult, withUnknown, none, duplicate },
            response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        Assert.Equal(0, await CountLedgerEntriesAsync(DemoDataIds.Fredster));
    }

    [Fact]
    public async Task Children_cannot_create_edit_delete_types_or_log_behaviours()
    {
        var typeBody = new { name = "Sneaky", description = "", points = 100 };
        var logBody = new
        {
            requestId = Guid.NewGuid(),
            typeId = DemoDataIds.BeingBrave,
            childIds = new[] { DemoDataIds.Fredster },
            points = (int?)null,
        };

        using var create = await SendAsync(
            HttpMethod.Post, "/api/good-behaviour-types", DemoDataIds.Fredster, typeBody);
        using var edit = await SendAsync(
            HttpMethod.Put, $"/api/good-behaviour-types/{DemoDataIds.BeingBrave}", DemoDataIds.Fredster, typeBody);
        using var delete = await SendAsync(
            HttpMethod.Delete, $"/api/good-behaviour-types/{DemoDataIds.BeingBrave}", DemoDataIds.Fredster);
        using var log = await SendAsync(
            HttpMethod.Post, "/api/good-behaviours", DemoDataIds.Fredster, logBody);

        Assert.All(
            new[] { create, edit, delete, log },
            response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
        Assert.Equal(0, await CountLedgerEntriesAsync(DemoDataIds.Fredster));
        var types = await GetTypesAsync(DemoDataIds.Fredster);
        Assert.Equal(3, types.Types.Count);
        Assert.DoesNotContain(types.Types, type => type.Name == "Sneaky");
    }

    [Fact]
    public async Task Editing_or_deleting_a_type_does_not_change_logged_history()
    {
        var type = await CreateTypeAsync("Sharing", "Shared a toy.", 6);
        using var logged = await LogAsync(type.Id, DemoDataIds.Harrie, points: 9);
        logged.EnsureSuccessStatusCode();

        using var edit = await SendAsync(
            HttpMethod.Put,
            $"/api/good-behaviour-types/{type.Id}",
            DemoDataIds.Addie,
            new { name = "Renamed", description = "Different words.", points = 1 });
        edit.EnsureSuccessStatusCode();
        using var delete = await SendAsync(
            HttpMethod.Delete, $"/api/good-behaviour-types/{type.Id}", DemoDataIds.Addie);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var board = await GetTodayAsync(DemoDataIds.Harrie);
        var earning = Assert.Single(board.PointEarnings);
        Assert.Equal("Sharing", earning.Name);
        Assert.Equal(9, earning.Points);
        Assert.Equal(9, board.PointsBalance);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var behaviour = await database.GoodBehaviours.SingleAsync(item => item.TypeId == type.Id);
        Assert.Equal("Sharing", behaviour.TypeName);
        Assert.Equal("Shared a toy.", behaviour.TypeDescription);
        var deletedType = await database.GoodBehaviourTypes.SingleAsync(item => item.Id == type.Id);
        Assert.False(deletedType.IsActive);
        Assert.Equal(DemoDataIds.Addie, deletedType.DeactivatedByMemberId);
    }

    [Fact]
    public async Task Deleted_types_are_hidden_cannot_be_logged_or_edited_and_delete_is_repeatable()
    {
        var type = await CreateTypeAsync("Temporary", "", 2);
        using var delete = await SendAsync(
            HttpMethod.Delete, $"/api/good-behaviour-types/{type.Id}", DemoDataIds.Addie);
        using var repeat = await SendAsync(
            HttpMethod.Delete, $"/api/good-behaviour-types/{type.Id}", DemoDataIds.Addie);
        using var missing = await SendAsync(
            HttpMethod.Delete, $"/api/good-behaviour-types/{Guid.NewGuid()}", DemoDataIds.Addie);
        using var edit = await SendAsync(
            HttpMethod.Put,
            $"/api/good-behaviour-types/{type.Id}",
            DemoDataIds.Addie,
            new { name = "Nope", description = "", points = 1 });
        using var log = await LogAsync(type.Id, DemoDataIds.Harrie, null);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeat.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, log.StatusCode);
        Assert.DoesNotContain(
            (await GetTypesAsync(DemoDataIds.Harrie)).Types,
            item => item.Id == type.Id);
    }

    [Fact]
    public async Task Invalid_requests_are_rejected_without_changing_the_ledger()
    {
        using var noName = await SendAsync(
            HttpMethod.Post,
            "/api/good-behaviour-types",
            DemoDataIds.Addie,
            new { name = " ", description = "", points = 1 });
        using var negativeType = await SendAsync(
            HttpMethod.Post,
            "/api/good-behaviour-types",
            DemoDataIds.Addie,
            new { name = "Ok", description = "", points = -1 });
        using var negativeLog = await LogAsync(DemoDataIds.BeingBrave, DemoDataIds.Harrie, -5);
        using var adultChild = await LogAsync(DemoDataIds.BeingBrave, DemoDataIds.Hellie, null);
        using var unknownType = await LogAsync(Guid.NewGuid(), DemoDataIds.Harrie, null);

        Assert.All(
            new[] { noName, negativeType, negativeLog, adultChild, unknownType },
            response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        Assert.Equal(0, await CountLedgerEntriesAsync(DemoDataIds.Harrie));
    }

    [Fact]
    public async Task Database_requires_each_ledger_entry_to_have_exactly_one_source()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        var neither = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO points_ledger_entries (id, child_id, amount, awarded_at_utc) "
            + $"VALUES (gen_random_uuid(), '{DemoDataIds.Harrie}', 1, now())"));
        var both = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO points_ledger_entries (id, child_id, job_id, good_behaviour_id, amount, awarded_at_utc) "
            + $"VALUES (gen_random_uuid(), '{DemoDataIds.Harrie}', '{DemoDataIds.FeedDog}', gen_random_uuid(), 1, now())"));

        Assert.Equal("ck_points_ledger_entries_single_source", neither.ConstraintName);
        Assert.NotNull(both);
    }

    [Fact]
    public async Task Resetting_jobs_and_points_removes_behaviour_logs_but_keeps_types()
    {
        await LogAsync(DemoDataIds.BeingBrave, DemoDataIds.Harrie, null);

        using var reset = await SendAsync(
            HttpMethod.Post,
            "/api/admin/jobs-and-points/reset",
            DemoDataIds.Addie,
            new { confirmation = "RESET TASKS AND POINTS" });

        Assert.True(reset.IsSuccessStatusCode, await reset.Content.ReadAsStringAsync());
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await database.GoodBehaviours.ToListAsync());
        Assert.Empty(await database.PointsLedgerEntries.ToListAsync());
        Assert.Equal(3, await database.GoodBehaviourTypes.CountAsync());
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountLedgerEntriesAsync(Guid childId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await database.PointsLedgerEntries.CountAsync(entry => entry.ChildId == childId);
    }

    private async Task<TypeResponse> CreateTypeAsync(string name, string description, int points)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "/api/good-behaviour-types",
            DemoDataIds.Addie,
            new { name, description, points });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TypeResponse>())!;
    }

    private async Task<TypesResponse> GetTypesAsync(Guid memberId)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/good-behaviour-types", memberId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TypesResponse>())!;
    }

    private async Task<BoardResponse> GetTodayAsync(Guid memberId)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/today", memberId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BoardResponse>())!;
    }

    private Task<HttpResponseMessage> LogAsync(
        Guid typeId,
        Guid childId,
        int? points,
        Guid? requestId = null) =>
        LogForAsync(typeId, [childId], points, requestId);

    private Task<HttpResponseMessage> LogForAsync(
        Guid typeId,
        Guid[] childIds,
        int? points,
        Guid? requestId = null)
    {
        return SendAsync(
            HttpMethod.Post,
            "/api/good-behaviours",
            DemoDataIds.Addie,
            new { requestId = requestId ?? Guid.NewGuid(), typeId, childIds, points });
    }

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

    private sealed record TypeResponse(Guid Id, string Name, string Description, int Points);

    private sealed record TypesResponse(IReadOnlyList<TypeResponse> Types);

    private sealed record BehaviourResponse(
        Guid Id,
        Guid TypeId,
        string TypeName,
        Guid ChildId,
        int Points);

    private sealed record AwardResponse(BehaviourResponse Behaviour, int PointsBalance);

    private sealed record LogResponse(IReadOnlyList<AwardResponse> Awards);

    private sealed record EarningResponse(
        Guid Id,
        string Source,
        string Name,
        Guid? JobId,
        int Points,
        string? LoggedByDisplayName);

    private sealed record BoardResponse(
        int? PointsBalance,
        IReadOnlyList<EarningResponse> PointEarnings);
}
