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

public sealed class PointAdjustmentEndpointsTests : IAsyncLifetime
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
    public async Task Adding_points_updates_the_balance_immediately_and_shows_in_the_childs_history()
    {
        using var response = await AdjustAsync(DemoDataIds.Fredster, 12, "Great effort at swimming");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AdjustmentResponse>();
        Assert.Equal(12, body!.PointsBalance);
        Assert.Equal(12, body.Adjustment.Amount);
        Assert.Equal(DemoDataIds.Addie, body.Adjustment.AdjustedByMemberId);

        var board = await GetTodayAsync(DemoDataIds.Fredster);
        Assert.Equal(12, board.PointsBalance);
        var earning = Assert.Single(board.PointEarnings);
        Assert.Equal("manualAdjustment", earning.Source);
        Assert.Equal("Great effort at swimming", earning.Name);
        Assert.Equal(12, earning.Points);
        Assert.Equal("Addie", earning.LoggedByDisplayName);
        Assert.Null(earning.JobId);
        Assert.Equal(0, (await GetTodayAsync(DemoDataIds.Harrie)).PointsBalance);
    }

    [Fact]
    public async Task Removing_points_within_the_balance_succeeds_without_confirmation()
    {
        (await AdjustAsync(DemoDataIds.Harrie, 10, "Bonus")).Dispose();

        using var response = await AdjustAsync(DemoDataIds.Harrie, -4, "Broke a plate");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var board = await GetTodayAsync(DemoDataIds.Harrie);
        Assert.Equal(6, board.PointsBalance);
        Assert.Equal([-4, 10], board.PointEarnings.Select(entry => entry.Points));
    }

    [Fact]
    public async Task Adjustments_appear_alongside_job_and_behaviour_points()
    {
        using var complete = await SendAsync(
            HttpMethod.Post, $"/api/jobs/{DemoDataIds.FeedDog}/complete", DemoDataIds.Fredster);
        complete.EnsureSuccessStatusCode();
        using var approve = await SendAsync(
            HttpMethod.Post, $"/api/jobs/{DemoDataIds.FeedDog}/approve", DemoDataIds.Addie);
        approve.EnsureSuccessStatusCode();
        using var behaviour = await SendAsync(
            HttpMethod.Post,
            "/api/good-behaviours",
            DemoDataIds.Addie,
            new
            {
                requestId = Guid.NewGuid(),
                typeId = DemoDataIds.BeingHelpful,
                childIds = new[] { DemoDataIds.Fredster },
                points = (int?)null,
            });
        behaviour.EnsureSuccessStatusCode();
        (await AdjustAsync(DemoDataIds.Fredster, -3, "Left the gate open", confirm: true)).Dispose();

        var board = await GetTodayAsync(DemoDataIds.Fredster);

        Assert.Equal(7, board.PointsBalance);
        Assert.Equal(
            ["manualAdjustment", "goodBehaviour", "job"],
            board.PointEarnings.Select(entry => entry.Source));
    }

    [Fact]
    public async Task Invalid_adjustments_are_rejected_and_change_nothing()
    {
        using var noReason = await AdjustAsync(DemoDataIds.Fredster, 5, "");
        using var blankReason = await AdjustAsync(DemoDataIds.Fredster, 5, "   ");
        using var nullReason = await AdjustAsync(DemoDataIds.Fredster, 5, null);
        using var zero = await AdjustAsync(DemoDataIds.Fredster, 0, "Nothing");
        using var adult = await AdjustAsync(DemoDataIds.Hellie, 5, "Not a child");
        using var unknown = await AdjustAsync(Guid.NewGuid(), 5, "Nobody");
        using var tooLong = await AdjustAsync(DemoDataIds.Fredster, 5, new string('a', 501));

        Assert.All(
            new[] { noReason, blankReason, nullReason, zero, adult, unknown, tooLong },
            response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        Assert.Equal(0, await CountLedgerEntriesAsync());
        Assert.Contains("Reason", await ErrorFieldsAsync(noReason));
        Assert.Contains("Amount", await ErrorFieldsAsync(zero));
    }

    [Fact]
    public async Task Taking_the_balance_negative_requires_confirmation_then_succeeds()
    {
        (await AdjustAsync(DemoDataIds.Fredster, 3, "Start")).Dispose();

        using var unconfirmed = await AdjustAsync(DemoDataIds.Fredster, -5, "Lost a library book");

        Assert.Equal(HttpStatusCode.Conflict, unconfirmed.StatusCode);
        var problem = await unconfirmed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("negativeBalanceConfirmationRequired", problem.GetProperty("code").GetString());
        Assert.Equal(3, problem.GetProperty("currentBalance").GetInt32());
        Assert.Equal(-2, problem.GetProperty("resultingBalance").GetInt32());
        Assert.Equal(1, await CountLedgerEntriesAsync());
        Assert.Equal(3, (await GetTodayAsync(DemoDataIds.Fredster)).PointsBalance);

        using var confirmed = await AdjustAsync(
            DemoDataIds.Fredster, -5, "Lost a library book", confirm: true);

        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
        var body = await confirmed.Content.ReadFromJsonAsync<AdjustmentResponse>();
        Assert.Equal(-2, body!.PointsBalance);
        var board = await GetTodayAsync(DemoDataIds.Fredster);
        Assert.Equal(-2, board.PointsBalance);
        Assert.Equal(-5, board.PointEarnings[0].Points);
    }

    [Fact]
    public async Task Retried_request_records_one_ledger_entry()
    {
        var requestId = Guid.NewGuid();

        using var first = await AdjustAsync(DemoDataIds.Fredster, 8, "Tidy room", requestId: requestId);
        using var retry = await AdjustAsync(DemoDataIds.Fredster, 8, " Tidy room ", requestId: requestId);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<AdjustmentResponse>();
        var retryBody = await retry.Content.ReadFromJsonAsync<AdjustmentResponse>();
        Assert.Equal(firstBody!.Adjustment.Id, retryBody!.Adjustment.Id);
        Assert.Equal(8, retryBody.PointsBalance);
        Assert.Equal(1, await CountLedgerEntriesAsync());
    }

    [Fact]
    public async Task Concurrent_duplicate_requests_record_one_ledger_entry()
    {
        var requestId = Guid.NewGuid();

        var statuses = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var response = await AdjustAsync(
                DemoDataIds.Harrie, 5, "Same request", requestId: requestId);
            return response.StatusCode;
        }));

        Assert.All(statuses, status =>
            Assert.True(status is HttpStatusCode.Created or HttpStatusCode.OK, status.ToString()));
        Assert.Equal(1, await CountLedgerEntriesAsync());
        Assert.Equal(5, (await GetTodayAsync(DemoDataIds.Harrie)).PointsBalance);
    }

    [Fact]
    public async Task Reusing_a_request_id_for_different_details_is_rejected()
    {
        var requestId = Guid.NewGuid();
        (await AdjustAsync(DemoDataIds.Fredster, 5, "First", requestId: requestId)).Dispose();

        using var different = await AdjustAsync(
            DemoDataIds.Fredster, 6, "First", requestId: requestId);

        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        var problem = await different.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("requestConflict", problem.GetProperty("code").GetString());
        Assert.Equal(1, await CountLedgerEntriesAsync());
    }

    [Fact]
    public async Task Children_cannot_record_adjustments()
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "/api/point-adjustments",
            DemoDataIds.Fredster,
            new
            {
                requestId = Guid.NewGuid(),
                childId = DemoDataIds.Fredster,
                amount = 1000,
                reason = "Sneaky",
                confirmNegativeBalance = false,
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await CountLedgerEntriesAsync());
    }

    [Fact]
    public async Task A_mistake_is_corrected_with_a_compensating_entry_and_history_is_preserved()
    {
        using var mistake = await AdjustAsync(DemoDataIds.Fredster, 20, "Typo");
        var mistakeBody = await mistake.Content.ReadFromJsonAsync<AdjustmentResponse>();

        using var correction = await AdjustAsync(
            DemoDataIds.Fredster, -20, "Correcting the mistaken +20");

        Assert.Equal(HttpStatusCode.Created, correction.StatusCode);
        var board = await GetTodayAsync(DemoDataIds.Fredster);
        Assert.Equal(0, board.PointsBalance);
        Assert.Equal(2, board.PointEarnings.Count);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var original = await database.PointAdjustments.SingleAsync(
            item => item.Id == mistakeBody!.Adjustment.Id);
        Assert.Equal(20, original.Amount);
        Assert.Equal("Typo", original.Reason);
        Assert.Equal(2, await database.PointsLedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Database_enforces_ledger_source_and_amount_rules()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        var child = DemoDataIds.Harrie;
        var adjustmentId = Guid.NewGuid();
        await ExecuteAsync(
            connection,
            "INSERT INTO point_adjustments (id, request_id, child_id, adjusted_by_member_id, amount, reason, adjusted_at_utc) "
            + $"VALUES ('{adjustmentId}', gen_random_uuid(), '{child}', '{DemoDataIds.Addie}', -3, 'Ok', now())");

        var negativeWithoutAdjustment = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO points_ledger_entries (id, child_id, job_id, amount, awarded_at_utc) "
            + $"VALUES (gen_random_uuid(), '{child}', '{DemoDataIds.FeedDog}', -1, now())"));
        var zeroAdjustment = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO points_ledger_entries (id, child_id, point_adjustment_id, amount, awarded_at_utc) "
            + $"VALUES (gen_random_uuid(), '{child}', '{adjustmentId}', 0, now())"));
        var twoSources = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO points_ledger_entries (id, child_id, job_id, point_adjustment_id, amount, awarded_at_utc) "
            + $"VALUES (gen_random_uuid(), '{child}', '{DemoDataIds.FeedDog}', '{adjustmentId}', 1, now())"));
        var zeroRecord = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO point_adjustments (id, request_id, child_id, adjusted_by_member_id, amount, reason, adjusted_at_utc) "
            + $"VALUES (gen_random_uuid(), gen_random_uuid(), '{child}', '{DemoDataIds.Addie}', 0, 'Ok', now())"));
        var blankReason = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO point_adjustments (id, request_id, child_id, adjusted_by_member_id, amount, reason, adjusted_at_utc) "
            + $"VALUES (gen_random_uuid(), gen_random_uuid(), '{child}', '{DemoDataIds.Addie}', 1, '   ', now())"));
        await ExecuteAsync(
            connection,
            "INSERT INTO points_ledger_entries (id, child_id, point_adjustment_id, amount, awarded_at_utc) "
            + $"VALUES (gen_random_uuid(), '{child}', '{adjustmentId}', -3, now())");
        var duplicateSource = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO points_ledger_entries (id, child_id, point_adjustment_id, amount, awarded_at_utc) "
            + $"VALUES (gen_random_uuid(), '{child}', '{adjustmentId}', -3, now())"));

        Assert.Equal("ck_points_ledger_entries_amount_sign", negativeWithoutAdjustment.ConstraintName);
        Assert.Equal("ck_points_ledger_entries_amount_sign", zeroAdjustment.ConstraintName);
        Assert.Equal("ck_points_ledger_entries_single_source", twoSources.ConstraintName);
        Assert.Equal("ck_point_adjustments_amount", zeroRecord.ConstraintName);
        Assert.Equal("ck_point_adjustments_reason", blankReason.ConstraintName);
        Assert.Equal("ux_points_ledger_entries_point_adjustment_id", duplicateSource.ConstraintName);
    }

    [Fact]
    public async Task Resetting_jobs_and_points_removes_adjustments()
    {
        (await AdjustAsync(DemoDataIds.Fredster, 5, "Before reset")).Dispose();

        using var reset = await SendAsync(
            HttpMethod.Post,
            "/api/admin/jobs-and-points/reset",
            DemoDataIds.Addie,
            new { confirmation = "RESET TASKS AND POINTS" });

        Assert.True(reset.IsSuccessStatusCode, await reset.Content.ReadAsStringAsync());
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await database.PointAdjustments.ToListAsync());
        Assert.Empty(await database.PointsLedgerEntries.ToListAsync());
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

    private async Task<BoardResponse> GetTodayAsync(Guid memberId)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/today", memberId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BoardResponse>())!;
    }

    private Task<HttpResponseMessage> AdjustAsync(
        Guid childId,
        int amount,
        string? reason,
        bool confirm = false,
        Guid? requestId = null)
    {
        return SendAsync(
            HttpMethod.Post,
            "/api/point-adjustments",
            DemoDataIds.Addie,
            new
            {
                requestId = requestId ?? Guid.NewGuid(),
                childId,
                amount,
                reason,
                confirmNegativeBalance = confirm,
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

    private sealed record AdjustmentRecord(
        Guid Id,
        Guid ChildId,
        Guid AdjustedByMemberId,
        int Amount,
        string Reason);

    private sealed record AdjustmentResponse(AdjustmentRecord Adjustment, int PointsBalance);

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
