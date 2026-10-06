using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.PointAdjustments;
using FamilyJobsBoard.Domain.Points;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace FamilyJobsBoard.Api.IntegrationTests;

public sealed class PointsLedgerEndpointsTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);

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
    public async Task An_adult_sees_every_childs_entries_newest_first_with_running_balances()
    {
        await CompleteAndApproveAsync(DemoDataIds.FeedDog, 5);
        using var behaviour = await SendAsync(
            HttpMethod.Post,
            "/api/good-behaviours",
            DemoDataIds.Addie,
            new
            {
                requestId = Guid.NewGuid(),
                typeId = DemoDataIds.BeingBrave,
                childIds = new[] { DemoDataIds.Harrie },
                points = (int?)null,
            });
        behaviour.EnsureSuccessStatusCode();
        using var adjustment = await SendAsync(
            HttpMethod.Post,
            "/api/point-adjustments",
            DemoDataIds.Addie,
            new
            {
                requestId = Guid.NewGuid(),
                childId = DemoDataIds.Fredster,
                amount = -2,
                reason = "Broke a plate",
            });
        adjustment.EnsureSuccessStatusCode();
        using var redemption = await RedeemAsync(DemoDataIds.Harrie, 4, "Ice cream", DemoDataIds.Hellie);
        redemption.EnsureSuccessStatusCode();

        var ledger = await GetLedgerAsync(_client!, DemoDataIds.Addie);

        Assert.Null(ledger.SelectedChildId);
        Assert.Null(ledger.NextCursor);
        Assert.Equal(
            [("Fredster", true, 3), ("Harrie", true, 6)],
            ledger.Children.Select(child => (child.DisplayName, child.IsActive, child.Balance)));
        Assert.Equal(
            [
                ("Ice cream", "Harrie", -4, 6, "Hellie"),
                ("Broke a plate", "Fredster", -2, 3, "Addie"),
                ("Being Brave", "Harrie", 10, 10, "Addie"),
                ("Feed the dog", "Fredster", 5, 5, null),
            ],
            ledger.Entries.Select(entry => (
                entry.Name,
                entry.ChildDisplayName,
                entry.Points,
                entry.BalanceAfter,
                entry.RecordedByDisplayName)));
        Assert.All(ledger.Entries, entry => Assert.NotEqual(default, entry.AwardedAtUtc));
    }

    [Fact]
    public async Task Recording_adults_stay_named_after_they_are_deactivated()
    {
        await SeedAdjustmentsAsync((DemoDataIds.Harrie, "Bonus", 6, DateTimeOffset.UtcNow.AddMinutes(-5)));
        using var redemption = await RedeemAsync(DemoDataIds.Harrie, 2, "Sticker", DemoDataIds.Hellie);
        redemption.EnsureSuccessStatusCode();
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hellie = await database.HouseholdMembers.SingleAsync(
                member => member.Id == DemoDataIds.Hellie);
            hellie.Deactivate(DemoDataIds.Addie, DateTimeOffset.UtcNow);
            await database.SaveChangesAsync();
        }

        var ledger = await GetLedgerAsync(_client!, DemoDataIds.Harrie);

        Assert.Equal(
            [("Sticker", "Hellie"), ("Bonus", "Addie")],
            ledger.Entries.Select(entry => (entry.Name, entry.RecordedByDisplayName)));
    }

    [Fact]
    public async Task An_adult_can_filter_the_ledger_to_one_child()
    {
        await SeedAdjustmentsAsync(
            (DemoDataIds.Fredster, "Fredster bonus", 4, Start),
            (DemoDataIds.Harrie, "Harrie bonus", 6, Start.AddMinutes(1)));

        var ledger = await GetLedgerAsync(_client!, DemoDataIds.Addie, $"childId={DemoDataIds.Harrie}");

        Assert.Equal(DemoDataIds.Harrie, ledger.SelectedChildId);
        Assert.Equal(2, ledger.Children.Count);
        var entry = Assert.Single(ledger.Entries);
        Assert.Equal(("Harrie bonus", 6, 6), (entry.Name, entry.Points, entry.BalanceAfter));
    }

    [Fact]
    public async Task A_child_sees_only_their_own_ledger_and_balance()
    {
        await SeedAdjustmentsAsync(
            (DemoDataIds.Fredster, "Fredster bonus", 4, Start),
            (DemoDataIds.Harrie, "Harrie bonus", 6, Start.AddMinutes(1)));

        var ledger = await GetLedgerAsync(_client!, DemoDataIds.Fredster);
        var ownFilter = await GetLedgerAsync(
            _client!,
            DemoDataIds.Fredster,
            $"childId={DemoDataIds.Fredster}");
        using var sibling = await SendAsync(
            HttpMethod.Get,
            $"/api/points-ledger?childId={DemoDataIds.Harrie}",
            DemoDataIds.Fredster);
        using var unknown = await SendAsync(
            HttpMethod.Get,
            $"/api/points-ledger?childId={Guid.NewGuid()}",
            DemoDataIds.Fredster);

        Assert.Equal(DemoDataIds.Fredster, ledger.SelectedChildId);
        var child = Assert.Single(ledger.Children);
        Assert.Equal((DemoDataIds.Fredster, 4), (child.Id, child.Balance));
        Assert.Equal("Fredster bonus", Assert.Single(ledger.Entries).Name);
        Assert.Equal(ledger.Entries.Select(entry => entry.Id), ownFilter.Entries.Select(entry => entry.Id));
        Assert.Equal(HttpStatusCode.Forbidden, sibling.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
    }

    [Fact]
    public async Task Paging_returns_every_entry_once_in_order_even_when_timestamps_tie()
    {
        var seeded = Enumerable.Range(0, 45)
            .Select(index => (
                ChildId: index % 3 == 0 ? DemoDataIds.Harrie : DemoDataIds.Fredster,
                Reason: $"Entry {index}",
                Amount: index % 5 == 0 ? -1 : 3,
                // Pairs share a timestamp, so page boundaries fall inside ties.
                At: Start.AddMinutes(index / 2)))
            .ToArray();
        await SeedAdjustmentsAsync(seeded);

        var entries = new List<LedgerEntryResponse>();
        var pageSizes = new List<int>();
        string? cursor = null;
        do
        {
            var page = await GetLedgerAsync(
                _client!,
                DemoDataIds.Addie,
                cursor is null ? "" : $"before={Uri.EscapeDataString(cursor)}");
            pageSizes.Add(page.Entries.Count);
            entries.AddRange(page.Entries);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal([20, 20, 5], pageSizes);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expectedOrder = await database.PointsLedgerEntries
            .OrderByDescending(entry => entry.AwardedAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Select(entry => entry.Id)
            .ToListAsync();
        Assert.Equal(expectedOrder, entries.Select(entry => entry.Id));
        foreach (var childId in new[] { DemoDataIds.Fredster, DemoDataIds.Harrie })
        {
            var childEntries = entries.Where(entry => entry.ChildId == childId).ToArray();
            Assert.Equal(
                seeded.Where(item => item.ChildId == childId).Sum(item => item.Amount),
                childEntries[0].BalanceAfter);
            for (var index = 0; index < childEntries.Length - 1; index++)
            {
                Assert.Equal(
                    childEntries[index].BalanceAfter - childEntries[index].Points,
                    childEntries[index + 1].BalanceAfter);
            }

            Assert.Equal(childEntries[^1].Points, childEntries[^1].BalanceAfter);
        }
    }

    [Fact]
    public async Task Deactivated_children_with_points_stay_listed_as_inactive()
    {
        var formerChildId = Guid.NewGuid();
        var neverEarnedId = Guid.NewGuid();
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.HouseholdMembers.Add(new HouseholdMember(formerChildId, "Anna", "Avenant", HouseholdRole.Child));
            database.HouseholdMembers.Add(new HouseholdMember(neverEarnedId, "Bert", "Avenant", HouseholdRole.Child));
            await database.SaveChangesAsync();
        }

        await SeedAdjustmentsAsync((formerChildId, "Tidied the garage", 7, Start));
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var member in await database.HouseholdMembers
                .Where(member => member.Id == formerChildId || member.Id == neverEarnedId)
                .ToListAsync())
            {
                member.Deactivate(DemoDataIds.Addie, Start.AddDays(1));
            }

            await database.SaveChangesAsync();
        }

        var ledger = await GetLedgerAsync(_client!, DemoDataIds.Addie);
        var filtered = await GetLedgerAsync(_client!, DemoDataIds.Addie, $"childId={formerChildId}");

        Assert.Equal(
            [("Fredster", true), ("Harrie", true), ("Anna", false)],
            ledger.Children.Select(child => (child.DisplayName, child.IsActive)));
        var entry = Assert.Single(ledger.Entries);
        Assert.Equal(("Anna", "Tidied the garage", 7), (entry.ChildDisplayName, entry.Name, entry.BalanceAfter));
        Assert.Equal(entry.Id, Assert.Single(filtered.Entries).Id);
    }

    [Theory]
    [InlineData("childId=00000000-0000-0000-0000-000000000001", "ChildId")]
    [InlineData("childId=9db319c1-28d1-4ce6-93d7-f04a45f8257d", "ChildId")]
    [InlineData("childId=not-a-guid", "ChildId")]
    [InlineData("before=not-a-cursor", "Before")]
    public async Task Invalid_adult_requests_return_validation_problems(string query, string field)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"/api/points-ledger?{query}",
            DemoDataIds.Addie);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), problem.ToString());
    }

    [Fact]
    public async Task The_daily_board_keeps_the_balance_but_no_longer_returns_the_history()
    {
        await SeedAdjustmentsAsync((DemoDataIds.Fredster, "Bonus", 4, Start));

        using var response = await SendAsync(HttpMethod.Get, "/api/today", DemoDataIds.Fredster);
        var board = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(4, board.GetProperty("pointsBalance").GetInt32());
        Assert.False(board.TryGetProperty("pointEarnings", out _));
    }

    internal static async Task<LedgerResponse> GetLedgerAsync(
        HttpClient client,
        Guid memberId,
        string query = "")
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            query.Length == 0 ? "/api/points-ledger" : $"/api/points-ledger?{query}");
        request.Headers.Add("X-Test-Member-Id", memberId.ToString());
        using var response = await client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LedgerResponse>())!;
    }

    private async Task SeedAdjustmentsAsync(
        params (Guid ChildId, string Reason, int Amount, DateTimeOffset At)[] entries)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var (childId, reason, amount, at) in entries)
        {
            var adjustment = new PointAdjustment(
                Guid.NewGuid(),
                Guid.NewGuid(),
                childId,
                DemoDataIds.Addie,
                amount,
                reason,
                at);
            database.PointAdjustments.Add(adjustment);
            database.PointsLedgerEntries.Add(PointsLedgerEntry.ForManualAdjustment(
                Guid.NewGuid(),
                childId,
                adjustment.Id,
                amount,
                at));
        }

        await database.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> RedeemAsync(
        Guid childId,
        int points,
        string reward,
        Guid adultId) =>
        SendAsync(
            HttpMethod.Post,
            "/api/point-redemptions",
            adultId,
            new { requestId = Guid.NewGuid(), childId, points, reward });

    private async Task CompleteAndApproveAsync(Guid jobId, int expectedPoints)
    {
        using var complete = await SendAsync(
            HttpMethod.Post, $"/api/jobs/{jobId}/complete", DemoDataIds.Fredster);
        complete.EnsureSuccessStatusCode();
        using var approve = await SendAsync(
            HttpMethod.Post,
            $"/api/jobs/{jobId}/approve?expectedPoints={expectedPoints}",
            DemoDataIds.Addie);
        approve.EnsureSuccessStatusCode();
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

    internal sealed record LedgerResponse(
        Guid? SelectedChildId,
        IReadOnlyList<LedgerChildResponse> Children,
        IReadOnlyList<LedgerEntryResponse> Entries,
        string? NextCursor);

    internal sealed record LedgerChildResponse(
        Guid Id,
        string DisplayName,
        bool IsActive,
        int Balance);

    internal sealed record LedgerEntryResponse(
        Guid Id,
        Guid ChildId,
        string ChildDisplayName,
        string Name,
        int Points,
        int BalanceAfter,
        DateTimeOffset AwardedAtUtc,
        string? RecordedByDisplayName);
}
