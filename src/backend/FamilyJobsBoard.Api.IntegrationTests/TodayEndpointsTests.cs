using System.Net;
using System.Net.Http.Json;
using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;

namespace FamilyJobsBoard.Api.IntegrationTests;

public sealed class TodayEndpointsTests : IAsyncLifetime
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
        _factory = new TestApiFactory(_postgres.GetConnectionString());
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
    public async Task Today_board_completion_is_persisted_and_repeat_is_rejected()
    {
        var client = _client ?? throw new InvalidOperationException("Test client was not initialised.");

        var initial = await client.GetFromJsonAsync<TodayResponse>("/api/today");

        Assert.NotNull(initial);
        Assert.Equal(DemoDataIds.Child, initial.Child.Id);
        Assert.Equal("Addie", initial.Child.FirstName);
        Assert.Null(initial.Child.Nickname);
        Assert.Equal("Addie", initial.Child.DisplayName);
        Assert.Equal(3, initial.Jobs.Count);
        Assert.Empty(initial.PointEarnings);
        Assert.All(initial.Jobs, job => Assert.Equal("open", job.Status));

        var target = initial.Jobs.Single(job => job.Id == DemoDataIds.FeedDog);
        using var completedResponse = await client.PostAsync($"/api/jobs/{target.Id}/complete", null);
        var completed = await completedResponse.Content.ReadFromJsonAsync<JobResponse>();

        Assert.Equal(HttpStatusCode.OK, completedResponse.StatusCode);
        Assert.NotNull(completed);
        Assert.Equal("pendingApproval", completed.Status);
        Assert.NotNull(completed.CompletedAtUtc);

        using var repeatResponse = await client.PostAsync($"/api/jobs/{target.Id}/complete", null);
        Assert.Equal(HttpStatusCode.Conflict, repeatResponse.StatusCode);

        var refreshed = await client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.NotNull(refreshed);
        Assert.Equal(
            "pendingApproval",
            refreshed.Jobs.Single(job => job.Id == target.Id).Status);
    }

    [Fact]
    public async Task Today_board_prefers_a_persisted_nickname_for_the_display_name()
    {
        var factory = _factory ?? throw new InvalidOperationException("Test API was not initialised.");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            const string nickname = "Ads";
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE household_members SET nickname = {nickname} WHERE id = {DemoDataIds.Child}");
        }

        var board = await Client.GetFromJsonAsync<TodayResponse>("/api/today");

        Assert.NotNull(board);
        Assert.Equal("Addie", board.Child.FirstName);
        Assert.Equal("Ads", board.Child.Nickname);
        Assert.Equal("Ads", board.Child.DisplayName);
    }

    [Fact]
    public async Task Nickname_migration_upgrades_the_existing_demo_child_name()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("family_jobs_board_upgrade_tests")
            .WithUsername("family_jobs_board")
            .WithPassword("family_jobs_board")
            .Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new AppDbContext(options);
        var migrator = database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260831101140_AddJobApprovalsAndPointsLedger");
        await database.Database.ExecuteSqlRawAsync(
            "INSERT INTO household_members (id, first_name, is_adult) " +
            "VALUES ('22eb0cc1-058e-4b2e-bb18-d7aaad564a6c', 'Alex', FALSE);");

        await migrator.MigrateAsync();
        database.ChangeTracker.Clear();
        var child = await database.HouseholdMembers.SingleAsync(
            member => member.Id == DemoDataIds.Child);

        Assert.Equal("Addie", child.FirstName);
        Assert.Null(child.Nickname);
    }

    [Fact]
    public async Task Added_job_is_trimmed_listed_persisted_and_can_be_completed()
    {
        var client = Client;
        using var createResponse = await client.PostAsJsonAsync(
            "/api/today/jobs",
            new
            {
                name = "  Put toys away  ",
                description = "  Return every toy to its box.  ",
                points = 4,
            });
        var created = await createResponse.Content.ReadFromJsonAsync<JobResponse>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Put toys away", created.Name);
        Assert.Equal("Return every toy to its box.", created.Description);
        Assert.Equal(4, created.Points);
        Assert.Equal("open", created.Status);
        Assert.Null(created.CompletedAtUtc);

        var listed = await client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.Contains(listed!.Jobs, job => job.Id == created.Id);

        await RestartApplicationAsync();

        var persisted = await Client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.Contains(persisted!.Jobs, job => job.Id == created.Id);

        using var completeResponse = await Client.PostAsync(
            $"/api/jobs/{created.Id}/complete",
            null);
        var completed = await completeResponse.Content.ReadFromJsonAsync<JobResponse>();

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        Assert.Equal("pendingApproval", completed!.Status);
    }

    [Fact]
    public async Task Pending_job_can_be_approved_once_and_award_persists_after_restart()
    {
        var initial = await Client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.NotNull(initial);
        Assert.Equal(0, initial.Child.PointsBalance);
        var target = initial.Jobs.Single(job => job.Id == DemoDataIds.FeedDog);

        using var missingResponse = await Client.PostAsync(
            $"/api/jobs/{Guid.NewGuid()}/approve",
            null);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

        using var openResponse = await Client.PostAsync(
            $"/api/jobs/{target.Id}/approve",
            null);
        Assert.Equal(HttpStatusCode.Conflict, openResponse.StatusCode);

        using var completeResponse = await Client.PostAsync(
            $"/api/jobs/{target.Id}/complete",
            null);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        using var approveResponse = await Client.PostAsync(
            $"/api/jobs/{target.Id}/approve",
            null);
        var approval = await approveResponse.Content.ReadFromJsonAsync<JobApprovalResponse>();

        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        Assert.NotNull(approval);
        Assert.Equal("approved", approval.Job.Status);
        Assert.NotNull(approval.Job.ApprovedAtUtc);
        Assert.Equal(target.Points, approval.PointsBalance);

        using var repeatResponse = await Client.PostAsync(
            $"/api/jobs/{target.Id}/approve",
            null);
        Assert.Equal(HttpStatusCode.Conflict, repeatResponse.StatusCode);

        await RestartApplicationAsync();

        var persisted = await Client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.NotNull(persisted);
        Assert.Equal(target.Points, persisted.Child.PointsBalance);
        var earning = Assert.Single(persisted.PointEarnings);
        Assert.NotEqual(Guid.Empty, earning.Id);
        Assert.Equal(target.Id, earning.JobId);
        Assert.Equal(target.Name, earning.JobName);
        Assert.Equal(target.Points, earning.Points);
        Assert.NotEqual(default, earning.AwardedAtUtc);
        Assert.Equal(
            "approved",
            persisted.Jobs.Single(job => job.Id == target.Id).Status);
    }

    [Fact]
    public async Task Pending_job_can_be_rejected_with_feedback_and_submitted_again()
    {
        var initial = await Client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.NotNull(initial);
        var target = initial.Jobs.Single(job => job.Id == DemoDataIds.FeedDog);

        using var rejectOpenResponse = await Client.PostAsJsonAsync(
            $"/api/jobs/{target.Id}/reject",
            new { reason = "Not finished." });
        Assert.Equal(HttpStatusCode.Conflict, rejectOpenResponse.StatusCode);

        using var completeResponse = await Client.PostAsync(
            $"/api/jobs/{target.Id}/complete",
            null);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        using var rejectResponse = await Client.PostAsJsonAsync(
            $"/api/jobs/{target.Id}/reject",
            new { reason = "  Please wipe underneath the bowl.  " });
        var rejected = await rejectResponse.Content.ReadFromJsonAsync<JobResponse>();

        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
        Assert.NotNull(rejected);
        Assert.Equal("open", rejected.Status);
        Assert.Null(rejected.CompletedAtUtc);
        Assert.Null(rejected.ApprovedAtUtc);
        Assert.NotNull(rejected.LatestRejection);
        Assert.Equal("Please wipe underneath the bowl.", rejected.LatestRejection.Reason);
        Assert.NotEqual(default, rejected.LatestRejection.RejectedAtUtc);

        using var repeatRejectResponse = await Client.PostAsJsonAsync(
            $"/api/jobs/{target.Id}/reject",
            new { reason = "Try again." });
        Assert.Equal(HttpStatusCode.Conflict, repeatRejectResponse.StatusCode);

        await RestartApplicationAsync();

        var persisted = await Client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.NotNull(persisted);
        Assert.Equal(0, persisted.Child.PointsBalance);
        Assert.Empty(persisted.PointEarnings);
        var reopened = persisted.Jobs.Single(job => job.Id == target.Id);
        Assert.Equal("open", reopened.Status);
        Assert.Equal(
            "Please wipe underneath the bowl.",
            reopened.LatestRejection?.Reason);

        using var resubmitResponse = await Client.PostAsync(
            $"/api/jobs/{target.Id}/complete",
            null);
        var resubmitted = await resubmitResponse.Content.ReadFromJsonAsync<JobResponse>();
        Assert.Equal(HttpStatusCode.OK, resubmitResponse.StatusCode);
        Assert.Equal("pendingApproval", resubmitted?.Status);
        Assert.Null(resubmitted?.LatestRejection);

        using var approveResponse = await Client.PostAsync(
            $"/api/jobs/{target.Id}/approve",
            null);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var finalBoard = await Client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.NotNull(finalBoard);
        Assert.Equal(target.Points, finalBoard.Child.PointsBalance);
        Assert.Single(finalBoard.PointEarnings);

        var factory = _factory ?? throw new InvalidOperationException("Test API was not initialised.");
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var decisions = await database.JobReviewDecisions
            .AsNoTracking()
            .Where(decision => decision.JobId == target.Id)
            .OrderBy(decision => decision.DecidedAtUtc)
            .ToListAsync();
        Assert.Collection(
            decisions,
            decision =>
            {
                Assert.Equal(JobReviewOutcome.Rejected, decision.Outcome);
                Assert.Equal("Please wipe underneath the bowl.", decision.Reason);
            },
            decision =>
            {
                Assert.Equal(JobReviewOutcome.Approved, decision.Outcome);
                Assert.Null(decision.Reason);
            });
    }

    [Fact]
    public async Task Rejection_accepts_no_reason_and_rejects_an_overlong_reason()
    {
        var initial = await Client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.NotNull(initial);
        var noReasonJob = initial.Jobs.Single(job => job.Id == DemoDataIds.FeedDog);
        var invalidReasonJob = initial.Jobs.Single(job => job.Id == DemoDataIds.PackBag);
        await CompleteAsync(noReasonJob.Id);
        await CompleteAsync(invalidReasonJob.Id);

        using var noReasonResponse = await Client.PostAsJsonAsync(
            $"/api/jobs/{noReasonJob.Id}/reject",
            new { reason = "   " });
        var noReason = await noReasonResponse.Content.ReadFromJsonAsync<JobResponse>();
        Assert.Equal(HttpStatusCode.OK, noReasonResponse.StatusCode);
        Assert.NotNull(noReason?.LatestRejection);
        Assert.Null(noReason.LatestRejection.Reason);

        using var invalidResponse = await Client.PostAsJsonAsync(
            $"/api/jobs/{invalidReasonJob.Id}/reject",
            new { reason = new string('r', JobReviewDecision.MaximumReasonLength + 1) });
        var problem = await invalidResponse.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Equal("Invalid rejection data", problem?.Title);
        Assert.Contains("Reason", problem?.Errors.Keys ?? []);

        var board = await Client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.Equal(
            "pendingApproval",
            board?.Jobs.Single(job => job.Id == invalidReasonJob.Id).Status);
        Assert.Equal(0, board?.Child.PointsBalance);
    }

    [Fact]
    public async Task Review_decision_migration_backfills_existing_approvals()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("family_jobs_board_review_upgrade_tests")
            .WithUsername("family_jobs_board")
            .WithPassword("family_jobs_board")
            .Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new AppDbContext(options);
        var migrator = database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260831113745_AddHouseholdMemberNickname");
        var jobId = Guid.NewGuid();
        var approvedAtUtc = new DateTimeOffset(2026, 8, 31, 10, 30, 0, TimeSpan.Zero);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO household_members (id, first_name, nickname, is_adult) VALUES ({DemoDataIds.Child}, 'Addie', NULL, FALSE);");
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO jobs (id, child_id, name, description, points, scheduled_date, status, completed_at_utc, approved_at_utc) VALUES ({jobId}, {DemoDataIds.Child}, 'Existing job', 'Already approved.', 4, DATE '2026-08-31', 'Approved', {approvedAtUtc}, {approvedAtUtc});");

        await migrator.MigrateAsync();
        database.ChangeTracker.Clear();
        var decision = await database.JobReviewDecisions.SingleAsync();

        Assert.Equal(jobId, decision.JobId);
        Assert.Equal(JobReviewOutcome.Approved, decision.Outcome);
        Assert.Null(decision.Reason);
        Assert.Equal(approvedAtUtc, decision.DecidedAtUtc);
    }

    [Fact]
    public async Task Point_earnings_are_newest_first_and_sum_to_the_balance()
    {
        var initial = await Client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.NotNull(initial);
        var first = initial.Jobs.Single(job => job.Id == DemoDataIds.FeedDog);
        var second = initial.Jobs.Single(job => job.Id == DemoDataIds.PackBag);

        await CompleteAndApproveAsync(first.Id);
        await CompleteAndApproveAsync(second.Id);

        var board = await Client.GetFromJsonAsync<TodayResponse>("/api/today");

        Assert.NotNull(board);
        Assert.Equal(first.Points + second.Points, board.Child.PointsBalance);
        Assert.Equal(board.Child.PointsBalance, board.PointEarnings.Sum(earning => earning.Points));
        Assert.Collection(
            board.PointEarnings,
            earning =>
            {
                Assert.Equal(second.Id, earning.JobId);
                Assert.Equal(second.Name, earning.JobName);
            },
            earning =>
            {
                Assert.Equal(first.Id, earning.JobId);
                Assert.Equal(first.Name, earning.JobName);
            });
    }

    [Fact]
    public async Task Concurrent_approval_requests_create_one_points_award()
    {
        using var completeResponse = await Client.PostAsync(
            $"/api/jobs/{DemoDataIds.FeedDog}/complete",
            null);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        var approvalRequests = new[]
        {
            Client.PostAsync($"/api/jobs/{DemoDataIds.FeedDog}/approve", null),
            Client.PostAsync($"/api/jobs/{DemoDataIds.FeedDog}/approve", null),
        };
        var responses = await Task.WhenAll(approvalRequests);

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        var board = await Client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.NotNull(board);
        Assert.Equal(5, board.Child.PointsBalance);
    }

    [Theory]
    [MemberData(nameof(InvalidJobs))]
    public async Task Invalid_job_is_rejected_with_problem_details(
        string name,
        string description,
        int points,
        string expectedField)
    {
        using var response = await Client.PostAsJsonAsync(
            "/api/today/jobs",
            new { name, description, points });
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("Invalid job data", problem.Title);
        Assert.Contains(expectedField, problem.Errors.Keys);
    }

    public static TheoryData<string, string, int, string> InvalidJobs => new()
    {
        { "   ", "Description", 1, "Name" },
        { new string('n', 161), "Description", 1, "Name" },
        { "Valid name", new string('d', 1001), 1, "Description" },
        { "Valid name", "Description", -1, "Points" },
    };

    private HttpClient Client =>
        _client ?? throw new InvalidOperationException("Test client was not initialised.");

    private async Task CompleteAndApproveAsync(Guid jobId)
    {
        using var completeResponse = await Client.PostAsync($"/api/jobs/{jobId}/complete", null);
        completeResponse.EnsureSuccessStatusCode();
        using var approveResponse = await Client.PostAsync($"/api/jobs/{jobId}/approve", null);
        approveResponse.EnsureSuccessStatusCode();
    }

    private async Task CompleteAsync(Guid jobId)
    {
        using var response = await Client.PostAsync($"/api/jobs/{jobId}/complete", null);
        response.EnsureSuccessStatusCode();
    }

    private async Task RestartApplicationAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        _factory = new TestApiFactory(_postgres.GetConnectionString());
        _client = _factory.CreateClient();
    }

    private sealed record TodayResponse(
        ChildResponse Child,
        DateOnly Date,
        IReadOnlyList<JobResponse> Jobs,
        IReadOnlyList<PointEarningResponse> PointEarnings);

    private sealed record ChildResponse(
        Guid Id,
        string FirstName,
        string? Nickname,
        string DisplayName,
        int PointsBalance);

    private sealed record JobResponse(
        Guid Id,
        string Name,
        string Description,
        int Points,
        string Status,
        DateTimeOffset? CompletedAtUtc,
        DateTimeOffset? ApprovedAtUtc,
        JobRejectionResponse? LatestRejection);

    private sealed record JobRejectionResponse(
        Guid DecisionId,
        string? Reason,
        DateTimeOffset RejectedAtUtc);

    private sealed record JobApprovalResponse(JobResponse Job, int PointsBalance);

    private sealed record PointEarningResponse(
        Guid Id,
        Guid JobId,
        string JobName,
        int Points,
        DateTimeOffset AwardedAtUtc);

    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public TestApiFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Database"] = _connectionString,
                    ["Household:TimeZone"] = "Africa/Johannesburg",
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_connectionString));
            });
        }
    }
}
