using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Domain.Points;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;
using static FamilyJobsBoard.Api.IntegrationTests.PointsLedgerEndpointsTests;
using static FamilyJobsBoard.Api.IntegrationTests.TodayEndpointsTests;

namespace FamilyJobsBoard.Api.IntegrationTests;

public sealed class JobApprovalReversalEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("family_jobs_board_tests")
        .WithUsername("family_jobs_board")
        .WithPassword("family_jobs_board")
        .Build();

    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    private HttpClient Client => _client
        ?? throw new InvalidOperationException("Test client was not initialised.");

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
    public async Task Reversing_a_submitted_approval_returns_the_job_for_approval_and_takes_back_its_points()
    {
        var completed = await CompleteAsChildAsync(DemoDataIds.FeedDog);
        await ApproveAsync(DemoDataIds.FeedDog, 5);
        var award = await SingleJobEntryAsync(DemoDataIds.FeedDog);

        using var response = await ReverseAsync(DemoDataIds.FeedDog, 5);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reversal = await response.Content.ReadFromJsonAsync<ReversalResponse>();
        Assert.NotNull(reversal);
        Assert.Equal("pendingApproval", reversal.Job.Status);
        Assert.Equal(completed.CompletedAtUtc, reversal.Job.CompletedAtUtc);
        Assert.Null(reversal.Job.ApprovedAtUtc);
        Assert.Equal(0, reversal.PointsBalance);

        var board = await GetBoardAsync(DemoDataIds.Addie);
        Assert.Equal(1, board.PendingApprovalCount);
        Assert.Equal(
            "pendingApproval",
            board.Jobs.Single(job => job.Id == DemoDataIds.FeedDog).Status);
        Assert.Equal(0, (await GetBoardAsync(DemoDataIds.Fredster)).PointsBalance);

        var ledger = await GetLedgerAsync(Client, DemoDataIds.Fredster);
        Assert.Collection(
            ledger.Entries,
            entry => Assert.Equal(
                ("Feed the dog", -5, 0, true, (string?)null),
                (entry.Name, entry.Points, entry.BalanceAfter, entry.IsApprovalReversal, entry.RecordedByDisplayName)),
            entry => Assert.Equal(
                (award.Id, "Feed the dog", 5, 5, false),
                (entry.Id, entry.Name, entry.Points, entry.BalanceAfter, entry.IsApprovalReversal)));

        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedAward = await database.PointsLedgerEntries.AsNoTracking()
            .SingleAsync(entry => entry.Id == award.Id);
        Assert.Equal((5, 1, award.AwardedAtUtc), (persistedAward.Amount, persistedAward.JobApprovalNumber, persistedAward.AwardedAtUtc));
        var reversalEntry = await database.PointsLedgerEntries.AsNoTracking()
            .SingleAsync(entry => entry.ReversedEntryId == award.Id);
        Assert.Equal((DemoDataIds.FeedDog, DemoDataIds.Fredster, -5), (reversalEntry.JobId, reversalEntry.ChildId, reversalEntry.Amount));
        Assert.Equal(
            [JobReviewOutcome.Approved, JobReviewOutcome.ApprovalReversed],
            await database.JobReviewDecisions
                .Where(decision => decision.JobId == DemoDataIds.FeedDog)
                .OrderBy(decision => decision.DecidedAtUtc)
                .Select(decision => decision.Outcome)
                .ToListAsync());
    }

    [Fact]
    public async Task Reversing_an_adult_completion_reopens_the_job_without_a_completion_time()
    {
        using var completion = await SendAsync(
            HttpMethod.Post,
            $"/api/jobs/{DemoDataIds.FeedDog}/complete?expectedPoints=5",
            DemoDataIds.Addie);
        Assert.Equal(HttpStatusCode.OK, completion.StatusCode);

        using var response = await ReverseAsync(DemoDataIds.FeedDog, 5);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reversal = await response.Content.ReadFromJsonAsync<ReversalResponse>();
        Assert.Equal("open", reversal!.Job.Status);
        Assert.Null(reversal.Job.CompletedAtUtc);
        Assert.Null(reversal.Job.ApprovedAtUtc);
        Assert.Equal(0, reversal.PointsBalance);
        var persisted = await GetJobAsync(DemoDataIds.FeedDog);
        Assert.Equal((JobStatus.Open, false, (DateTimeOffset?)null), (persisted.Status, persisted.CompletedByAdult, persisted.CompletedAtUtc));
        Assert.Equal(0, (await GetBoardAsync(DemoDataIds.Addie)).PendingApprovalCount);

        // The child can do the job and send it for approval like any open job.
        await CompleteAsChildAsync(DemoDataIds.FeedDog);
        Assert.Equal(JobStatus.PendingApproval, (await GetJobAsync(DemoDataIds.FeedDog)).Status);
    }

    [Fact]
    public async Task A_reversed_job_awards_its_points_once_more_each_time_it_is_approved_again()
    {
        await CompleteAsChildAsync(DemoDataIds.FeedDog);
        await ApproveAsync(DemoDataIds.FeedDog, 5);
        (await ReverseAsync(DemoDataIds.FeedDog, 5)).EnsureSuccessStatusCode();

        using var repeatReversal = await ReverseAsync(DemoDataIds.FeedDog, 5);
        Assert.Equal(HttpStatusCode.Conflict, repeatReversal.StatusCode);

        await ApproveAsync(DemoDataIds.FeedDog, 5);
        using var repeatApproval = await SendAsync(
            HttpMethod.Post,
            $"/api/jobs/{DemoDataIds.FeedDog}/approve?expectedPoints=5",
            DemoDataIds.Addie);
        Assert.Equal(HttpStatusCode.Conflict, repeatApproval.StatusCode);
        Assert.Equal(5, (await GetBoardAsync(DemoDataIds.Fredster)).PointsBalance);

        (await ReverseAsync(DemoDataIds.FeedDog, 5)).EnsureSuccessStatusCode();
        await ApproveAsync(DemoDataIds.FeedDog, 5);

        Assert.Equal(5, (await GetBoardAsync(DemoDataIds.Fredster)).PointsBalance);
        var ledger = await GetLedgerAsync(Client, DemoDataIds.Fredster);
        Assert.Equal([5, -5, 5, -5, 5], ledger.Entries.Reverse().Select(entry => entry.Points));
        Assert.Equal([5, 0, 5, 0, 5], ledger.Entries.Reverse().Select(entry => entry.BalanceAfter));
        Assert.Equal(3, (await GetJobAsync(DemoDataIds.FeedDog)).ApprovalCount);
    }

    [Fact]
    public async Task Renaming_a_reversed_job_keeps_its_earlier_history_named_as_it_was()
    {
        await CompleteAsChildAsync(DemoDataIds.FeedDog);
        await ApproveAsync(DemoDataIds.FeedDog, 5);
        (await ReverseAsync(DemoDataIds.FeedDog, 5)).EnsureSuccessStatusCode();
        using var edit = await SendAsync(
            HttpMethod.Put,
            $"/api/jobs/{DemoDataIds.FeedDog}",
            DemoDataIds.Addie,
            new
            {
                name = "Walk the dog",
                description = "Round the block.",
                points = 6,
                scheduledDate = (await GetJobAsync(DemoDataIds.FeedDog)).ScheduledDate,
                agendaPeriod = "morning",
                scheduledTime = (string?)null,
            });
        Assert.True(edit.IsSuccessStatusCode, await edit.Content.ReadAsStringAsync());

        await ApproveAsync(DemoDataIds.FeedDog, 6);

        var ledger = await GetLedgerAsync(Client, DemoDataIds.Fredster);
        Assert.Equal(
            [("Feed the dog", 5, false), ("Feed the dog", -5, true), ("Walk the dog", 6, false)],
            ledger.Entries.Reverse().Select(entry => (entry.Name, entry.Points, entry.IsApprovalReversal)));
        Assert.Equal(6, (await GetBoardAsync(DemoDataIds.Fredster)).PointsBalance);
    }

    [Fact]
    public async Task A_reversed_job_can_be_rejected_or_cancelled()
    {
        foreach (var jobId in new[] { DemoDataIds.FeedDog, DemoDataIds.PackBag })
        {
            await CompleteAsChildAsync(jobId);
        }

        await ApproveAsync(DemoDataIds.FeedDog, 5);
        await ApproveAsync(DemoDataIds.PackBag, 8);
        (await ReverseAsync(DemoDataIds.FeedDog, 5)).EnsureSuccessStatusCode();
        (await ReverseAsync(DemoDataIds.PackBag, 8)).EnsureSuccessStatusCode();

        using var rejection = await SendAsync(
            HttpMethod.Post,
            $"/api/jobs/{DemoDataIds.FeedDog}/reject",
            DemoDataIds.Addie,
            new { reason = "The water bowl was empty." });
        using var cancellation = await SendAsync(
            HttpMethod.Post,
            $"/api/jobs/{DemoDataIds.PackBag}/cancel",
            DemoDataIds.Addie,
            new { reason = "No school tomorrow." });

        Assert.Equal(HttpStatusCode.OK, rejection.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cancellation.StatusCode);
        var board = await GetBoardAsync(DemoDataIds.Fredster);
        var rejected = board.Jobs.Single(job => job.Id == DemoDataIds.FeedDog);
        Assert.Equal("open", rejected.Status);
        Assert.Equal("The water bowl was empty.", rejected.LatestRejection?.Reason);
        Assert.DoesNotContain(board.Jobs, job => job.Id == DemoDataIds.PackBag);
        Assert.Equal(0, board.PointsBalance);
    }

    [Fact]
    public async Task Reversal_is_refused_when_the_points_were_already_redeemed()
    {
        await CompleteAsChildAsync(DemoDataIds.FeedDog);
        await ApproveAsync(DemoDataIds.FeedDog, 5);
        using var redemption = await SendAsync(
            HttpMethod.Post,
            "/api/point-redemptions",
            DemoDataIds.Addie,
            new { requestId = Guid.NewGuid(), childId = DemoDataIds.Fredster, points = 3, reward = "Ice cream" });
        redemption.EnsureSuccessStatusCode();
        var entriesBefore = await CountLedgerEntriesAsync();

        using var response = await ReverseAsync(DemoDataIds.FeedDog, 5);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("insufficientPoints", problem.GetProperty("code").GetString());
        Assert.Equal(2, problem.GetProperty("currentBalance").GetInt32());
        Assert.Equal(
            "Fredster only has 2 points, so reversing this approval can't take back its 5 points. Nothing was changed.",
            problem.GetProperty("detail").GetString());
        Assert.Equal(JobStatus.Approved, (await GetJobAsync(DemoDataIds.FeedDog)).Status);
        Assert.Equal(entriesBefore, await CountLedgerEntriesAsync());
        Assert.Equal(1, await CountDecisionsAsync(DemoDataIds.FeedDog));
        Assert.Equal(2, (await GetBoardAsync(DemoDataIds.Fredster)).PointsBalance);
    }

    [Fact]
    public async Task Concurrent_reversals_of_the_same_approval_record_one_reversal()
    {
        await GiveAsync(DemoDataIds.Fredster, 20);
        await CompleteAsChildAsync(DemoDataIds.FeedDog);
        await ApproveAsync(DemoDataIds.FeedDog, 5);

        var statuses = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            using var response = await ReverseAsync(DemoDataIds.FeedDog, 5);
            return response.StatusCode;
        }));

        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(3, statuses.Count(status => status == HttpStatusCode.Conflict));
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await database.PointsLedgerEntries.CountAsync(entry => entry.ReversedEntryId != null));
        Assert.Equal(1, await database.JobReviewDecisions.CountAsync(
            decision => decision.Outcome == JobReviewOutcome.ApprovalReversed));
        Assert.Equal(20, (await GetBoardAsync(DemoDataIds.Fredster)).PointsBalance);
    }

    [Fact]
    public async Task A_reversal_and_a_redemption_cannot_both_spend_the_same_points()
    {
        await CompleteAsChildAsync(DemoDataIds.FeedDog);
        await ApproveAsync(DemoDataIds.FeedDog, 5);

        var statuses = await Task.WhenAll(
            Task.Run(async () =>
            {
                using var response = await ReverseAsync(DemoDataIds.FeedDog, 5);
                return response.StatusCode;
            }),
            Task.Run(async () =>
            {
                using var response = await SendAsync(
                    HttpMethod.Post,
                    "/api/point-redemptions",
                    DemoDataIds.Addie,
                    new { requestId = Guid.NewGuid(), childId = DemoDataIds.Fredster, points = 5, reward = "Movie" });
                return response.StatusCode;
            }));

        Assert.Equal(1, statuses.Count(status => status is HttpStatusCode.OK or HttpStatusCode.Created));
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Conflict));
        Assert.Equal(0, (await GetBoardAsync(DemoDataIds.Fredster)).PointsBalance);
    }

    [Fact]
    public async Task Children_cannot_reverse_approvals()
    {
        await CompleteAsChildAsync(DemoDataIds.FeedDog);
        await ApproveAsync(DemoDataIds.FeedDog, 5);

        using var response = await SendAsync(
            HttpMethod.Post,
            $"/api/jobs/{DemoDataIds.FeedDog}/reverse-approval?expectedPoints=5",
            DemoDataIds.Fredster);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(JobStatus.Approved, (await GetJobAsync(DemoDataIds.FeedDog)).Status);
        Assert.Equal(5, (await GetBoardAsync(DemoDataIds.Fredster)).PointsBalance);
    }

    [Fact]
    public async Task Invalid_reversals_return_the_same_problems_as_other_job_transitions()
    {
        using var unknown = await ReverseAsync(Guid.NewGuid(), 5);
        using var open = await ReverseAsync(DemoDataIds.FeedDog, 5);
        await CompleteAsChildAsync(DemoDataIds.FeedDog);
        using var pending = await ReverseAsync(DemoDataIds.FeedDog, 5);
        await ApproveAsync(DemoDataIds.FeedDog, 5);
        using var missingPoints = await SendAsync(
            HttpMethod.Post,
            $"/api/jobs/{DemoDataIds.FeedDog}/reverse-approval",
            DemoDataIds.Addie);
        using var stalePoints = await ReverseAsync(DemoDataIds.FeedDog, 4);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, open.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, pending.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingPoints.StatusCode);
        var validation = await missingPoints.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Invalid approval reversal", validation.GetProperty("title").GetString());
        Assert.True(validation.GetProperty("errors").TryGetProperty("expectedPoints", out _));
        Assert.Equal(HttpStatusCode.Conflict, stalePoints.StatusCode);
        Assert.Equal(JobStatus.Approved, (await GetJobAsync(DemoDataIds.FeedDog)).Status);

        await DeactivateFredsterAsync();
        using var inactiveChild = await ReverseAsync(DemoDataIds.FeedDog, 5);
        Assert.Equal(HttpStatusCode.NotFound, inactiveChild.StatusCode);
    }

    [Fact]
    public async Task Database_enforces_one_award_per_approval_and_one_reversal_per_award()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        var child = DemoDataIds.Fredster;
        var job = DemoDataIds.FeedDog;
        var award = Guid.NewGuid();
        await ExecuteAsync(connection, LedgerInsert(award, child, job, approvalNumber: "1", reversed: null, amount: 5));

        var duplicateAward = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection, LedgerInsert(Guid.NewGuid(), child, job, "1", null, 5)));
        var unnumberedAward = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection, LedgerInsert(Guid.NewGuid(), child, job, "NULL", null, 5)));
        var positiveReversal = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection, LedgerInsert(Guid.NewGuid(), child, job, "NULL", award, 5)));
        var numberedReversal = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection, LedgerInsert(Guid.NewGuid(), child, job, "2", award, -5)));
        var missingAward = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection, LedgerInsert(Guid.NewGuid(), child, job, "NULL", Guid.NewGuid(), -5)));
        var unnamedAward = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO points_ledger_entries (id, child_id, job_id, job_approval_number, amount, awarded_at_utc) "
            + $"VALUES (gen_random_uuid(), '{child}', '{job}', 2, 5, now())"));
        var behaviourReversal = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO points_ledger_entries (id, child_id, good_behaviour_id, reversed_entry_id, amount, awarded_at_utc) "
            + $"VALUES (gen_random_uuid(), '{child}', gen_random_uuid(), '{award}', -5, now())"));
        await ExecuteAsync(connection, LedgerInsert(Guid.NewGuid(), child, job, "NULL", award, -5));
        var secondReversal = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection, LedgerInsert(Guid.NewGuid(), child, job, "NULL", award, -5)));
        await ExecuteAsync(connection, LedgerInsert(Guid.NewGuid(), child, job, "2", null, 5));
        var reversedAwardDeleted = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection, $"DELETE FROM points_ledger_entries WHERE id = '{award}'"));

        Assert.Equal("ux_points_ledger_entries_job_approval", duplicateAward.ConstraintName);
        Assert.Equal("ck_points_ledger_entries_job_entry", unnumberedAward.ConstraintName);
        Assert.Equal("ck_points_ledger_entries_amount_sign", positiveReversal.ConstraintName);
        Assert.Equal("ck_points_ledger_entries_job_entry", numberedReversal.ConstraintName);
        Assert.Equal("FK_points_ledger_entries_reversed_entry_id", missingAward.ConstraintName);
        Assert.Equal("ck_points_ledger_entries_job_name", unnamedAward.ConstraintName);
        Assert.Equal("ck_points_ledger_entries_job_entry", behaviourReversal.ConstraintName);
        Assert.Equal("ux_points_ledger_entries_reversed_entry_id", secondReversal.ConstraintName);
        Assert.Equal("FK_points_ledger_entries_reversed_entry_id", reversedAwardDeleted.ConstraintName);
    }

    [Fact]
    public async Task Database_keeps_job_approval_tracking_consistent()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        var negativeCount = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection, $"UPDATE jobs SET approval_count = -1 WHERE id = '{DemoDataIds.FeedDog}'"));
        var approvedWithoutApproval = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            $"UPDATE jobs SET status = 'Approved', completed_at_utc = now(), approved_at_utc = now() WHERE id = '{DemoDataIds.FeedDog}'"));
        var openAdultCompletion = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection, $"UPDATE jobs SET completed_by_adult = TRUE WHERE id = '{DemoDataIds.FeedDog}'"));

        Assert.All(
            new[] { negativeCount, approvedWithoutApproval, openAdultCompletion },
            exception => Assert.Equal("ck_jobs_approval_tracking", exception.ConstraintName));
    }

    [Fact]
    public async Task Resetting_jobs_and_points_removes_awards_and_their_reversals()
    {
        await CompleteAsChildAsync(DemoDataIds.FeedDog);
        await ApproveAsync(DemoDataIds.FeedDog, 5);
        (await ReverseAsync(DemoDataIds.FeedDog, 5)).EnsureSuccessStatusCode();

        using var reset = await SendAsync(
            HttpMethod.Post,
            "/api/admin/jobs-and-points/reset",
            DemoDataIds.Addie,
            new { confirmation = "RESET TASKS AND POINTS" });

        Assert.True(reset.IsSuccessStatusCode, await reset.Content.ReadAsStringAsync());
        Assert.Equal(0, await CountLedgerEntriesAsync());
    }

    [Fact]
    public async Task Migration_backfills_existing_approvals_so_they_can_be_reversed_and_refuses_a_lossy_downgrade()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("family_jobs_board_reversal_upgrade_tests")
            .WithUsername("family_jobs_board")
            .WithPassword("family_jobs_board")
            .Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new AppDbContext(options);
        var migrator = database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var childId = Guid.NewGuid();
        var submittedJobId = Guid.NewGuid();
        var adultCompletedJobId = Guid.NewGuid();
        var openJobId = Guid.NewGuid();
        var completedAtUtc = new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);
        var approvedAtUtc = completedAtUtc.AddMinutes(30);
        var today = new DateOnly(2026, 10, 13);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO household_members (id, first_name, nickname, surname, role, is_active) VALUES ({childId}, 'Fredster', NULL, 'Avenant', 'Child', TRUE);");
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO jobs (id, child_id, name, description, points, scheduled_date, agenda_period, status, completed_at_utc, approved_at_utc) VALUES ({submittedJobId}, {childId}, 'Submitted', '', 4, {today}, 'Unscheduled', 'Approved', {completedAtUtc}, {approvedAtUtc}), ({adultCompletedJobId}, {childId}, 'Marked done', '', 6, {today}, 'Unscheduled', 'Approved', {completedAtUtc}, {completedAtUtc}), ({openJobId}, {childId}, 'Still open', '', 2, {today}, 'Unscheduled', 'Open', NULL, NULL);");
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO points_ledger_entries (id, child_id, job_id, amount, awarded_at_utc) VALUES ({Guid.NewGuid()}, {childId}, {submittedJobId}, 4, {approvedAtUtc}), ({Guid.NewGuid()}, {childId}, {adultCompletedJobId}, 6, {completedAtUtc});");

        await migrator.MigrateAsync();
        database.ChangeTracker.Clear();

        var jobs = await database.Jobs.AsNoTracking().ToDictionaryAsync(job => job.Id);
        Assert.Equal((1, false), (jobs[submittedJobId].ApprovalCount, jobs[submittedJobId].CompletedByAdult));
        Assert.Equal((1, true), (jobs[adultCompletedJobId].ApprovalCount, jobs[adultCompletedJobId].CompletedByAdult));
        Assert.Equal((0, false), (jobs[openJobId].ApprovalCount, jobs[openJobId].CompletedByAdult));
        Assert.Equal(
            [("Marked done", 1), ("Submitted", 1)],
            (await database.PointsLedgerEntries.AsNoTracking().ToListAsync())
                .Select(entry => (entry.JobName, entry.JobApprovalNumber ?? 0))
                .OrderBy(entry => entry.JobName));

        // The backfilled award is the one a reversal takes back, and both reverse as they began.
        var submitted = await database.Jobs.SingleAsync(job => job.Id == submittedJobId);
        var adultCompleted = await database.Jobs.SingleAsync(job => job.Id == adultCompletedJobId);
        var award = await database.PointsLedgerEntries.AsNoTracking().SingleAsync(entry =>
            entry.JobId == submittedJobId && entry.JobApprovalNumber == submitted.ApprovalCount);
        submitted.ReverseApproval();
        adultCompleted.ReverseApproval();
        database.PointsLedgerEntries.Add(PointsLedgerEntry.ForApprovalReversal(
            Guid.NewGuid(), award, approvedAtUtc.AddHours(1)));
        await database.SaveChangesAsync();
        Assert.Equal((JobStatus.PendingApproval, completedAtUtc), (submitted.Status, submitted.CompletedAtUtc));
        Assert.Equal((JobStatus.Open, (DateTimeOffset?)null), (adultCompleted.Status, adultCompleted.CompletedAtUtc));

        var downgradeError = await Assert.ThrowsAsync<PostgresException>(
            () => migrator.MigrateAsync(PreviousMigration));
        Assert.Equal(PostgresErrorCodes.RaiseException, downgradeError.SqlState);
        Assert.Contains("Cannot downgrade while reversed job approvals exist", downgradeError.MessageText);
    }

    private const string PreviousMigration = "20261005145639_AddPointRedemptions";

    private static string LedgerInsert(
        Guid id,
        Guid childId,
        Guid jobId,
        string approvalNumber,
        Guid? reversed,
        int amount) =>
        "INSERT INTO points_ledger_entries (id, child_id, job_id, job_name, job_approval_number, reversed_entry_id, amount, awarded_at_utc) "
        + $"VALUES ('{id}', '{childId}', '{jobId}', 'Feed the dog', {approvalNumber}, {(reversed is null ? "NULL" : $"'{reversed}'")}, {amount}, now())";

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<JobResponse> CompleteAsChildAsync(Guid jobId)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"/api/jobs/{jobId}/complete",
            DemoDataIds.Fredster);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JobResponse>())!;
    }

    private async Task ApproveAsync(Guid jobId, int expectedPoints)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"/api/jobs/{jobId}/approve?expectedPoints={expectedPoints}",
            DemoDataIds.Addie);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private Task<HttpResponseMessage> ReverseAsync(Guid jobId, int expectedPoints) =>
        SendAsync(
            HttpMethod.Post,
            $"/api/jobs/{jobId}/reverse-approval?expectedPoints={expectedPoints}",
            DemoDataIds.Addie);

    private async Task GiveAsync(Guid childId, int points)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "/api/point-adjustments",
            DemoDataIds.Addie,
            new { requestId = Guid.NewGuid(), childId, amount = points, reason = "Starting points" });
        response.EnsureSuccessStatusCode();
    }

    private async Task DeactivateFredsterAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE household_members SET is_active = FALSE WHERE id = {DemoDataIds.Fredster}");
    }

    private async Task<TodayResponse> GetBoardAsync(Guid memberId)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/today", memberId);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TodayResponse>())!;
    }

    private async Task<Job> GetJobAsync(Guid jobId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await database.Jobs.AsNoTracking().SingleAsync(job => job.Id == jobId);
    }

    private async Task<PointsLedgerEntry> SingleJobEntryAsync(Guid jobId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await database.PointsLedgerEntries.AsNoTracking().SingleAsync(entry => entry.JobId == jobId);
    }

    private async Task<int> CountLedgerEntriesAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await database.PointsLedgerEntries.CountAsync();
    }

    private async Task<int> CountDecisionsAsync(Guid jobId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await database.JobReviewDecisions.CountAsync(decision => decision.JobId == jobId);
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

        return Client.SendAsync(request);
    }

    private sealed record ReversalResponse(JobResponse Job, int PointsBalance);
}
