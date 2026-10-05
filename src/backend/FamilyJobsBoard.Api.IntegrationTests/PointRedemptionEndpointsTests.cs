using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace FamilyJobsBoard.Api.IntegrationTests;

public sealed class PointRedemptionEndpointsTests : IAsyncLifetime
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
            new DateOnly(2026, 10, 5),
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
    public async Task Redeeming_spends_points_and_shows_in_the_childs_history_with_the_redeeming_adult()
    {
        await GiveAsync(DemoDataIds.Fredster, 12);

        using var response = await RedeemAsync(
            DemoDataIds.Fredster, 5, "  Ice cream  ", adultId: DemoDataIds.Hellie);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RedemptionResponse>();
        Assert.Equal(7, body!.PointsBalance);
        Assert.Equal(5, body.Redemption.Points);
        Assert.Equal("Ice cream", body.Redemption.Reward);
        Assert.Equal(DemoDataIds.Fredster, body.Redemption.ChildId);
        Assert.Equal(DemoDataIds.Hellie, body.Redemption.RedeemedByMemberId);
        Assert.Equal(7, (await GetTodayAsync(DemoDataIds.Fredster)).PointsBalance);
        var childView = await PointsLedgerEndpointsTests.GetLedgerAsync(_client!, DemoDataIds.Fredster);
        var newest = childView.Entries[0];
        Assert.Equal(
            ("Ice cream", -5, 7, "Hellie"),
            (newest.Name, newest.Points, newest.BalanceAfter, newest.RecordedByDisplayName));
    }

    [Fact]
    public async Task Redeeming_more_than_the_balance_is_rejected_with_the_current_balance()
    {
        await GiveAsync(DemoDataIds.Fredster, 4);

        using var response = await RedeemAsync(DemoDataIds.Fredster, 5, "Movie night");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("insufficientPoints", problem.GetProperty("code").GetString());
        Assert.Equal(4, problem.GetProperty("currentBalance").GetInt32());
        Assert.Equal("Fredster only has 4 points.", problem.GetProperty("detail").GetString());
        Assert.Equal(1, await CountLedgerEntriesAsync());
        Assert.Empty(await RedemptionsAsync());
    }

    [Fact]
    public async Task Redeeming_exactly_the_balance_leaves_zero()
    {
        await GiveAsync(DemoDataIds.Harrie, 9);

        using var response = await RedeemAsync(DemoDataIds.Harrie, 9, "Toy car");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(0, (await GetTodayAsync(DemoDataIds.Harrie)).PointsBalance);
    }

    [Fact]
    public async Task Invalid_redemptions_are_rejected_and_change_nothing()
    {
        await GiveAsync(DemoDataIds.Fredster, 50);

        using var noReward = await RedeemAsync(DemoDataIds.Fredster, 5, "");
        using var blankReward = await RedeemAsync(DemoDataIds.Fredster, 5, "   ");
        using var nullReward = await RedeemAsync(DemoDataIds.Fredster, 5, null);
        using var zero = await RedeemAsync(DemoDataIds.Fredster, 0, "Nothing");
        using var negative = await RedeemAsync(DemoDataIds.Fredster, -5, "Backwards");
        using var adult = await RedeemAsync(DemoDataIds.Hellie, 5, "Not a child");
        using var unknown = await RedeemAsync(Guid.NewGuid(), 5, "Nobody");
        using var tooLong = await RedeemAsync(DemoDataIds.Fredster, 5, new string('a', 201));
        using var noRequest = await RedeemAsync(DemoDataIds.Fredster, 5, "Ice cream", requestId: Guid.Empty);

        Assert.All(
            new[] { noReward, blankReward, nullReward, zero, negative, adult, unknown, tooLong, noRequest },
            response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        Assert.Empty(await RedemptionsAsync());
        Assert.Contains("Reward", await ErrorFieldsAsync(noReward));
        Assert.Contains("Points", await ErrorFieldsAsync(zero));
        Assert.Contains("ChildId", await ErrorFieldsAsync(adult));
        Assert.Contains("RequestId", await ErrorFieldsAsync(noRequest));
    }

    [Fact]
    public async Task Retried_request_records_one_ledger_entry()
    {
        await GiveAsync(DemoDataIds.Fredster, 8);
        var requestId = Guid.NewGuid();

        using var first = await RedeemAsync(DemoDataIds.Fredster, 8, "Ice cream", requestId: requestId);
        using var retry = await RedeemAsync(DemoDataIds.Fredster, 8, " Ice cream ", requestId: requestId);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<RedemptionResponse>();
        var retryBody = await retry.Content.ReadFromJsonAsync<RedemptionResponse>();
        Assert.Equal(firstBody!.Redemption.Id, retryBody!.Redemption.Id);
        Assert.Equal(0, retryBody.PointsBalance);
        Assert.Single(await RedemptionsAsync());
    }

    [Fact]
    public async Task Concurrent_duplicate_requests_record_one_ledger_entry()
    {
        await GiveAsync(DemoDataIds.Harrie, 5);
        var requestId = Guid.NewGuid();

        var statuses = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var response = await RedeemAsync(
                DemoDataIds.Harrie, 5, "Same request", requestId: requestId);
            return response.StatusCode;
        }));

        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(5, statuses.Count(status => status == HttpStatusCode.OK));
        Assert.Single(await RedemptionsAsync());
        Assert.Equal(0, (await GetTodayAsync(DemoDataIds.Harrie)).PointsBalance);
    }

    [Fact]
    public async Task Concurrent_redemptions_only_spend_the_points_the_child_has()
    {
        await GiveAsync(DemoDataIds.Harrie, 10);

        var statuses = await Task.WhenAll(Enumerable.Range(0, 5).Select(async index =>
        {
            using var response = await RedeemAsync(DemoDataIds.Harrie, 4, $"Reward {index}");
            return response.StatusCode;
        }));

        Assert.Equal(2, statuses.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(3, statuses.Count(status => status == HttpStatusCode.Conflict));
        Assert.Equal(2, (await GetTodayAsync(DemoDataIds.Harrie)).PointsBalance);
    }

    [Fact]
    public async Task A_concurrent_adjustment_and_redemption_cannot_take_the_balance_negative()
    {
        await GiveAsync(DemoDataIds.Fredster, 5);

        var statuses = await Task.WhenAll(
            Task.Run(async () =>
            {
                using var response = await AdjustAsync(DemoDataIds.Fredster, -4, "Broke a plate");
                return response.StatusCode;
            }),
            Task.Run(async () =>
            {
                using var response = await RedeemAsync(DemoDataIds.Fredster, 4, "Ice cream");
                return response.StatusCode;
            }));

        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Conflict));
        Assert.Equal(1, (await GetTodayAsync(DemoDataIds.Fredster)).PointsBalance);
    }

    [Fact]
    public async Task Reusing_a_request_id_for_different_details_is_rejected()
    {
        await GiveAsync(DemoDataIds.Fredster, 20);
        var requestId = Guid.NewGuid();
        (await RedeemAsync(DemoDataIds.Fredster, 5, "Ice cream", requestId: requestId)).Dispose();

        using var different = await RedeemAsync(
            DemoDataIds.Fredster, 6, "Ice cream", requestId: requestId);
        using var otherChild = await RedeemAsync(
            DemoDataIds.Harrie, 5, "Ice cream", requestId: requestId);

        foreach (var response in new[] { different, otherChild })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("requestConflict", problem.GetProperty("code").GetString());
        }

        Assert.Single(await RedemptionsAsync());
    }

    [Fact]
    public async Task Children_cannot_redeem()
    {
        await GiveAsync(DemoDataIds.Fredster, 20);

        using var response = await RedeemAsync(
            DemoDataIds.Fredster, 5, "Sneaky treat", adultId: DemoDataIds.Fredster);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await RedemptionsAsync());
    }

    [Fact]
    public async Task Database_enforces_redemption_and_ledger_rules()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        var child = DemoDataIds.Harrie;
        var redemptionId = Guid.NewGuid();
        await ExecuteAsync(
            connection,
            "INSERT INTO point_redemptions (id, request_id, child_id, redeemed_by_member_id, points, reward, redeemed_at_utc) "
            + $"VALUES ('{redemptionId}', gen_random_uuid(), '{child}', '{DemoDataIds.Addie}', 3, 'Ok', now())");

        var positiveRedemption = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO points_ledger_entries (id, child_id, point_redemption_id, amount, awarded_at_utc) "
            + $"VALUES (gen_random_uuid(), '{child}', '{redemptionId}', 3, now())"));
        var twoSources = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO points_ledger_entries (id, child_id, job_id, point_redemption_id, amount, awarded_at_utc) "
            + $"VALUES (gen_random_uuid(), '{child}', '{DemoDataIds.FeedDog}', '{redemptionId}', -3, now())"));
        var zeroPoints = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO point_redemptions (id, request_id, child_id, redeemed_by_member_id, points, reward, redeemed_at_utc) "
            + $"VALUES (gen_random_uuid(), gen_random_uuid(), '{child}', '{DemoDataIds.Addie}', 0, 'Ok', now())"));
        var blankReward = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO point_redemptions (id, request_id, child_id, redeemed_by_member_id, points, reward, redeemed_at_utc) "
            + $"VALUES (gen_random_uuid(), gen_random_uuid(), '{child}', '{DemoDataIds.Addie}', 1, '   ', now())"));
        await ExecuteAsync(
            connection,
            "INSERT INTO points_ledger_entries (id, child_id, point_redemption_id, amount, awarded_at_utc) "
            + $"VALUES (gen_random_uuid(), '{child}', '{redemptionId}', -3, now())");
        var duplicateSource = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO points_ledger_entries (id, child_id, point_redemption_id, amount, awarded_at_utc) "
            + $"VALUES (gen_random_uuid(), '{child}', '{redemptionId}', -3, now())"));

        Assert.Equal("ck_points_ledger_entries_amount_sign", positiveRedemption.ConstraintName);
        Assert.Equal("ck_points_ledger_entries_single_source", twoSources.ConstraintName);
        Assert.Equal("ck_point_redemptions_points", zeroPoints.ConstraintName);
        Assert.Equal("ck_point_redemptions_reward", blankReward.ConstraintName);
        Assert.Equal("ux_points_ledger_entries_point_redemption_id", duplicateSource.ConstraintName);
    }

    [Fact]
    public async Task Resetting_jobs_and_points_removes_redemptions()
    {
        await GiveAsync(DemoDataIds.Fredster, 5);
        (await RedeemAsync(DemoDataIds.Fredster, 5, "Before reset")).Dispose();

        using var reset = await SendAsync(
            HttpMethod.Post,
            "/api/admin/jobs-and-points/reset",
            DemoDataIds.Addie,
            new { confirmation = "RESET TASKS AND POINTS" });

        Assert.True(reset.IsSuccessStatusCode, await reset.Content.ReadAsStringAsync());
        Assert.Empty(await RedemptionsAsync());
        Assert.Equal(0, await CountLedgerEntriesAsync());
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyCollection<string>> ErrorFieldsAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.GetProperty("errors").EnumerateObject().Select(item => item.Name).ToArray();
    }

    private async Task<int> CountLedgerEntriesAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await database.PointsLedgerEntries.CountAsync();
    }

    private async Task<IReadOnlyList<Guid>> RedemptionsAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await database.PointRedemptions.Select(redemption => redemption.Id).ToListAsync();
    }

    private async Task<BoardResponse> GetTodayAsync(Guid memberId)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/today", memberId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BoardResponse>())!;
    }

    private async Task GiveAsync(Guid childId, int points)
    {
        using var response = await AdjustAsync(childId, points, "Starting points");
        response.EnsureSuccessStatusCode();
    }

    private Task<HttpResponseMessage> AdjustAsync(Guid childId, int amount, string reason) =>
        SendAsync(
            HttpMethod.Post,
            "/api/point-adjustments",
            DemoDataIds.Addie,
            new { requestId = Guid.NewGuid(), childId, amount, reason });

    private Task<HttpResponseMessage> RedeemAsync(
        Guid childId,
        int points,
        string? reward,
        Guid? requestId = null,
        Guid? adultId = null)
    {
        return SendAsync(
            HttpMethod.Post,
            "/api/point-redemptions",
            adultId ?? DemoDataIds.Addie,
            new
            {
                requestId = requestId ?? Guid.NewGuid(),
                childId,
                points,
                reward,
            });
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

    private sealed record RedemptionRecord(
        Guid Id,
        Guid ChildId,
        Guid RedeemedByMemberId,
        int Points,
        string Reward);

    private sealed record RedemptionResponse(RedemptionRecord Redemption, int PointsBalance);

    private sealed record BoardResponse(int? PointsBalance);
}
