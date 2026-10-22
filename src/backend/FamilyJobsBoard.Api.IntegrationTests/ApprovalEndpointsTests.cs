using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace FamilyJobsBoard.Api.IntegrationTests;

public sealed class ApprovalEndpointsTests : IAsyncLifetime
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
    public async Task Pending_approvals_include_work_from_earlier_days_oldest_first()
    {
        var lastWeek = await SeedJobAsync(DemoDataIds.Harrie, "Last week's chore", CurrentDate.AddDays(-7), submitted: true);
        var yesterday = await SeedJobAsync(DemoDataIds.Fredster, "Yesterday's chore", CurrentDate.AddDays(-1), submitted: true);
        var stillOpen = await SeedJobAsync(DemoDataIds.Harrie, "Not done yet", CurrentDate.AddDays(-2), submitted: false);

        var approvals = await GetPendingApprovalsAsync();

        Assert.All(approvals.Jobs, job => Assert.Equal("pendingApproval", job.Status));
        Assert.DoesNotContain(approvals.Jobs, job => job.Id == stillOpen);
        var seeded = approvals.Jobs.Where(job => job.Id == lastWeek || job.Id == yesterday).ToArray();
        Assert.Equal([lastWeek, yesterday], seeded.Select(job => job.Id));
        Assert.Equal(
            approvals.Jobs.Select(job => job.ScheduledDate).Order(),
            approvals.Jobs.Select(job => job.ScheduledDate));
        Assert.Equal("Fredster", seeded[1].ChildDisplayName);
    }

    [Fact]
    public async Task Today_and_calendar_counts_cover_every_pending_job_whatever_the_date_or_filter()
    {
        await SeedJobAsync(DemoDataIds.Harrie, "Yesterday's chore", CurrentDate.AddDays(-1), submitted: true);
        await SeedJobAsync(DemoDataIds.Fredster, "Next week's chore", CurrentDate.AddDays(7), submitted: true);
        var expected = (await GetPendingApprovalsAsync()).Jobs.Count;

        var today = await GetJsonAsync($"/api/today?date={CurrentDate:yyyy-MM-dd}");
        var filtered = await GetJsonAsync($"/api/today?date={CurrentDate:yyyy-MM-dd}&childId={DemoDataIds.Fredster}");
        var calendar = await GetJsonAsync($"/api/calendar?view=day&date={CurrentDate.AddDays(-30):yyyy-MM-dd}");

        Assert.True(expected >= 2);
        Assert.Equal(expected, today.GetProperty("pendingApprovalCount").GetInt32());
        Assert.Equal(expected, filtered.GetProperty("pendingApprovalCount").GetInt32());
        Assert.Equal(expected, calendar.GetProperty("pendingApprovalCount").GetInt32());
    }

    [Fact]
    public async Task Approving_an_earlier_days_job_removes_it_from_the_queue_and_the_count()
    {
        var yesterday = await SeedJobAsync(DemoDataIds.Harrie, "Yesterday's chore", CurrentDate.AddDays(-1), submitted: true);
        var before = (await GetPendingApprovalsAsync()).Jobs.Count;

        using var approve = await SendAsync(
            HttpMethod.Post,
            $"/api/jobs/{yesterday}/approve?expectedPoints=3",
            DemoDataIds.Addie);
        approve.EnsureSuccessStatusCode();

        var after = await GetPendingApprovalsAsync();
        var today = await GetJsonAsync($"/api/today?date={CurrentDate:yyyy-MM-dd}");
        Assert.DoesNotContain(after.Jobs, job => job.Id == yesterday);
        Assert.Equal(before - 1, after.Jobs.Count);
        Assert.Equal(before - 1, today.GetProperty("pendingApprovalCount").GetInt32());
    }

    [Fact]
    public async Task Pending_approvals_leave_out_cancelled_jobs_and_inactive_children()
    {
        var cancelled = await SeedJobAsync(DemoDataIds.Harrie, "Cancelled chore", CurrentDate.AddDays(-1), submitted: true);
        using var cancel = await SendAsync(
            HttpMethod.Post,
            $"/api/jobs/{cancelled}/cancel",
            DemoDataIds.Addie,
            new { reason = (string?)null });
        cancel.EnsureSuccessStatusCode();
        var inactiveChildsJob = await SeedJobAsync(DemoDataIds.Fredster, "Fredster's chore", CurrentDate.AddDays(-1), submitted: true);
        using var deactivate = await SendAsync(
            HttpMethod.Delete,
            $"/api/users/{DemoDataIds.Fredster}",
            DemoDataIds.Addie);
        deactivate.EnsureSuccessStatusCode();

        var approvals = await GetPendingApprovalsAsync();

        Assert.DoesNotContain(approvals.Jobs, job => job.Id == cancelled);
        Assert.DoesNotContain(approvals.Jobs, job => job.Id == inactiveChildsJob);
    }

    [Fact]
    public async Task Children_cannot_read_the_review_queue()
    {
        using var response = await SendAsync(
            HttpMethod.Get, "/api/approvals/pending", DemoDataIds.Fredster);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private DateOnly CurrentDate => (_factory
        ?? throw new InvalidOperationException("Test API was not initialised."))
        .Services.GetRequiredService<IHouseholdClock>()
        .Today;

    private async Task<Guid> SeedJobAsync(Guid childId, string name, DateOnly scheduledDate, bool submitted)
    {
        // Past-dated work can't be created through the API, but it is exactly the state a job
        // reaches when a child submits it and the household day moves on before review.
        var job = new Job(Guid.NewGuid(), childId, name, "", 3, scheduledDate);
        if (submitted)
        {
            job.MarkComplete(DateTimeOffset.UtcNow);
        }

        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.Jobs.Add(job);
        await database.SaveChangesAsync();
        return job.Id;
    }

    private async Task<PendingApprovalsResponse> GetPendingApprovalsAsync()
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/approvals/pending", DemoDataIds.Addie);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PendingApprovalsResponse>())!;
    }

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        using var response = await SendAsync(HttpMethod.Get, url, DemoDataIds.Addie);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
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

    private sealed record PendingApprovalsResponse(IReadOnlyList<TodayEndpointsTests.JobResponse> Jobs);
}
