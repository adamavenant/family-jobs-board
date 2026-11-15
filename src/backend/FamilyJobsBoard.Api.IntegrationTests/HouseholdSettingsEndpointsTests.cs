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

public sealed class HouseholdSettingsEndpointsTests : IAsyncLifetime
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
            DateOnly.FromDateTime(DateTime.UtcNow),
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
    public async Task A_new_household_gives_everyone_thirty_seconds_to_undo()
    {
        var adult = await GetSettingsAsync(DemoDataIds.Addie);
        var child = await GetSettingsAsync(DemoDataIds.Fredster);

        Assert.Equal(new SettingsResponse(30, 5, 120), adult);
        Assert.Equal(adult, child);
        Assert.Equal(30, (await GetBoardAsync(DemoDataIds.Addie)).UndoGracePeriodSeconds);
        Assert.Equal(30, (await GetBoardAsync(DemoDataIds.Fredster)).UndoGracePeriodSeconds);
        Assert.Equal(0, await CountSettingsRowsAsync());
    }

    [Fact]
    public async Task An_adult_changes_the_undo_time_for_every_device_in_the_household()
    {
        using var response = await PutAsync(DemoDataIds.Addie, new { undoGracePeriodSeconds = 45 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new SettingsResponse(45, 5, 120),
            await response.Content.ReadFromJsonAsync<SettingsResponse>());
        Assert.Equal(45, (await GetSettingsAsync(DemoDataIds.Hellie)).UndoGracePeriodSeconds);
        Assert.Equal(45, (await GetSettingsAsync(DemoDataIds.Harrie)).UndoGracePeriodSeconds);
        Assert.Equal(45, (await GetBoardAsync(DemoDataIds.Hellie)).UndoGracePeriodSeconds);
        Assert.Equal(45, (await GetBoardAsync(DemoDataIds.Harrie)).UndoGracePeriodSeconds);

        using var again = await PutAsync(DemoDataIds.Hellie, new { undoGracePeriodSeconds = 120 });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(120, (await GetSettingsAsync(DemoDataIds.Fredster)).UndoGracePeriodSeconds);

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT id, undo_grace_period_seconds, updated_by_member_id, updated_at_utc FROM household_settings",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(120, reader.GetInt32(1));
        Assert.Equal(DemoDataIds.Hellie, reader.GetGuid(2));
        Assert.False(reader.IsDBNull(3));
        Assert.False(await reader.ReadAsync());
    }

    [Theory]
    [InlineData(4)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(121)]
    [InlineData(null)]
    public async Task Undo_time_outside_five_to_one_hundred_twenty_seconds_is_a_validation_error(
        int? seconds)
    {
        using var response = await PutAsync(DemoDataIds.Addie, new { undoGracePeriodSeconds = seconds });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(
            "UndoGracePeriodSeconds",
            problem.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        Assert.Equal(30, (await GetSettingsAsync(DemoDataIds.Addie)).UndoGracePeriodSeconds);
    }

    [Fact]
    public async Task A_fractional_undo_time_is_rejected()
    {
        using var response = await _client!.SendAsync(Request(
            HttpMethod.Put,
            DemoDataIds.Addie,
            new StringContent(
                """{"undoGracePeriodSeconds":12.5}""",
                System.Text.Encoding.UTF8,
                "application/json")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(30, (await GetSettingsAsync(DemoDataIds.Addie)).UndoGracePeriodSeconds);
    }

    [Fact]
    public async Task Children_can_read_but_not_change_the_undo_time()
    {
        using var response = await PutAsync(DemoDataIds.Fredster, new { undoGracePeriodSeconds = 60 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(30, (await GetSettingsAsync(DemoDataIds.Fredster)).UndoGracePeriodSeconds);
        Assert.Equal(0, await CountSettingsRowsAsync());
    }

    [Fact]
    public async Task Simultaneous_first_changes_both_succeed_and_keep_one_settings_row()
    {
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(index =>
                PutAsync(DemoDataIds.Addie, new { undoGracePeriodSeconds = 10 + index })));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        foreach (var response in responses)
        {
            response.Dispose();
        }

        Assert.Equal(1, await CountSettingsRowsAsync());
        Assert.InRange((await GetSettingsAsync(DemoDataIds.Addie)).UndoGracePeriodSeconds, 10, 15);
    }

    [Fact]
    public async Task Database_keeps_one_settings_row_with_an_undo_time_in_range()
    {
        await using var connection = await OpenAsync();

        var outOfRange = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO household_settings (id, undo_grace_period_seconds) VALUES (1, 121)"));
        var secondRow = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO household_settings (id, undo_grace_period_seconds) VALUES (2, 30)"));
        await ExecuteAsync(
            connection,
            "INSERT INTO household_settings (id, undo_grace_period_seconds) VALUES (1, 5)");
        var tooShort = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "UPDATE household_settings SET undo_grace_period_seconds = 4"));
        var unknownAdult = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            $"UPDATE household_settings SET updated_by_member_id = '{Guid.NewGuid()}'"));

        Assert.Equal("ck_household_settings_undo_grace_period_seconds", outOfRange.ConstraintName);
        Assert.Equal("ck_household_settings_singleton", secondRow.ConstraintName);
        Assert.Equal("ck_household_settings_undo_grace_period_seconds", tooShort.ConstraintName);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, unknownAdult.SqlState);
        Assert.Equal(5, (await GetSettingsAsync(DemoDataIds.Addie)).UndoGracePeriodSeconds);
    }

    [Fact]
    public async Task Resetting_jobs_and_points_keeps_the_undo_time()
    {
        (await PutAsync(DemoDataIds.Addie, new { undoGracePeriodSeconds = 15 })).Dispose();

        using var reset = await _client!.SendAsync(Request(
            HttpMethod.Post,
            DemoDataIds.Addie,
            JsonContent.Create(new { confirmation = "RESET TASKS AND POINTS" }),
            "/api/admin/jobs-and-points/reset"));

        Assert.True(reset.IsSuccessStatusCode, await reset.Content.ReadAsStringAsync());
        Assert.Equal(15, (await GetSettingsAsync(DemoDataIds.Addie)).UndoGracePeriodSeconds);
    }

    private async Task<SettingsResponse> GetSettingsAsync(Guid memberId)
    {
        using var response = await _client!.SendAsync(Request(HttpMethod.Get, memberId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SettingsResponse>())!;
    }

    private async Task<BoardResponse> GetBoardAsync(Guid memberId)
    {
        using var response = await _client!.SendAsync(
            Request(HttpMethod.Get, memberId, path: "/api/today"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BoardResponse>())!;
    }

    private Task<HttpResponseMessage> PutAsync(Guid memberId, object body) =>
        _client!.SendAsync(Request(HttpMethod.Put, memberId, JsonContent.Create(body)));

    private static HttpRequestMessage Request(
        HttpMethod method,
        Guid memberId,
        HttpContent? content = null,
        string path = "/api/household-settings")
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("X-Test-Member-Id", memberId.ToString());
        return request;
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    private async Task<long> CountSettingsRowsAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM household_settings",
            connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record SettingsResponse(
        int UndoGracePeriodSeconds,
        int MinimumUndoGracePeriodSeconds,
        int MaximumUndoGracePeriodSeconds);

    private sealed record BoardResponse(int UndoGracePeriodSeconds);
}
