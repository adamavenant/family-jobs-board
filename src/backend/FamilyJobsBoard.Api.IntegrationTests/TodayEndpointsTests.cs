using System.Net;
using System.Net.Http.Json;
using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Identity;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
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
        Assert.Equal(DemoDataIds.Fredster, initial.Viewer.Id);
        Assert.Equal("Fredster", initial.Viewer.FirstName);
        Assert.False(initial.Viewer.IsAdult);
        Assert.Equal(4, initial.Members.Count);
        Assert.Equal(0, initial.PointsBalance);
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
    public async Task Adult_edit_is_persisted_without_creating_a_ledger_entry()
    {
        var jobId = DemoDataIds.FeedDog;
        using var response = await Client.PutAsJsonAsync(
            $"/api/jobs/{jobId}",
            new
            {
                name = "Feed and water the dog",
                description = "Fresh water and one scoop of food.",
                points = 7,
                scheduledDate = CurrentDate.AddDays(1),
                agendaPeriod = "evening",
                scheduledTime = "18:15:00",
            });
        var updated = await response.Content.ReadFromJsonAsync<JobResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(updated);
        Assert.Equal("Feed and water the dog", updated.Name);
        Assert.Equal(CurrentDate.AddDays(1), updated.ScheduledDate);
        Assert.Equal("evening", updated.AgendaPeriod);
        Assert.Equal(new TimeOnly(18, 15), updated.ScheduledTime);

        await using var scope = (_factory
            ?? throw new InvalidOperationException("Test API was not initialised."))
            .Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(7, (await database.Jobs.SingleAsync(job => job.Id == jobId)).Points);
        Assert.False(await database.PointsLedgerEntries.AnyAsync(entry => entry.JobId == jobId));
    }

    [Fact]
    public async Task Approved_job_is_immutable_for_edit_and_cancel()
    {
        await CompleteAndApproveAsync(DemoDataIds.FeedDog);

        using var edit = await Client.PutAsJsonAsync(
            $"/api/jobs/{DemoDataIds.FeedDog}",
            new
            {
                name = "Changed",
                description = "",
                points = 99,
                scheduledDate = CurrentDate,
                agendaPeriod = "morning",
                scheduledTime = (string?)null,
            });
        using var cancel = await Client.PostAsJsonAsync(
            $"/api/jobs/{DemoDataIds.FeedDog}/cancel",
            new { reason = "Too late." });

        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        await using var scope = (_factory
            ?? throw new InvalidOperationException("Test API was not initialised."))
            .Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await database.Jobs.SingleAsync(item => item.Id == DemoDataIds.FeedDog);
        Assert.Equal(JobStatus.Approved, job.Status);
        Assert.Equal(5, await database.PointsLedgerEntries
            .Where(entry => entry.JobId == job.Id)
            .SumAsync(entry => entry.Amount));
    }

    [Fact]
    public async Task Adult_can_cancel_pending_job_while_preserving_review_history_and_awarding_no_points()
    {
        await CompleteAsync(DemoDataIds.PackBag);
        using (var reject = await Client.PostAsJsonAsync(
            $"/api/jobs/{DemoDataIds.PackBag}/reject",
            new { reason = "Please check the lunchbox." }))
        {
            reject.EnsureSuccessStatusCode();
        }
        await CompleteAsync(DemoDataIds.PackBag);

        using var cancel = await Client.PostAsJsonAsync(
            $"/api/jobs/{DemoDataIds.PackBag}/cancel",
            new { reason = "School is closed." });
        var cancelled = await cancel.Content.ReadFromJsonAsync<JobResponse>();

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.NotNull(cancelled);
        Assert.Equal("cancelled", cancelled.Status);

        var adultBoard = await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Addie}");
        var childBoard = await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Fredster}");
        Assert.NotNull(adultBoard);
        Assert.NotNull(childBoard);
        Assert.DoesNotContain(adultBoard.Jobs, job => job.Id == DemoDataIds.PackBag);
        Assert.DoesNotContain(childBoard.Jobs, job => job.Id == DemoDataIds.PackBag);

        await using var scope = (_factory
            ?? throw new InvalidOperationException("Test API was not initialised."))
            .Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await database.Jobs.SingleAsync(job => job.Id == DemoDataIds.PackBag);
        Assert.Equal(JobStatus.Cancelled, stored.Status);
        Assert.Equal(DemoDataIds.Addie, stored.CancelledByMemberId);
        Assert.Equal("School is closed.", stored.CancellationReason);
        Assert.NotNull(stored.CancelledAtUtc);
        Assert.Single(await database.JobReviewDecisions
            .Where(decision => decision.JobId == stored.Id)
            .ToListAsync());
        Assert.False(await database.PointsLedgerEntries.AnyAsync(entry => entry.JobId == stored.Id));
    }

    [Fact]
    public async Task Child_cannot_edit_or_cancel_a_job()
    {
        using var editRequest = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/jobs/{DemoDataIds.FeedDog}")
        {
            Content = JsonContent.Create(new
            {
                name = "Changed",
                description = "",
                points = 2,
                scheduledDate = CurrentDate,
                agendaPeriod = "morning",
                scheduledTime = (string?)null,
            }),
        };
        editRequest.Headers.Add("X-Test-Member-Id", DemoDataIds.Fredster.ToString());
        using var edit = await Client.SendAsync(editRequest);

        using var cancelRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/jobs/{DemoDataIds.FeedDog}/cancel")
        {
            Content = JsonContent.Create(new { reason = "No." }),
        };
        cancelRequest.Headers.Add("X-Test-Member-Id", DemoDataIds.Fredster.ToString());
        using var cancel = await Client.SendAsync(cancelRequest);

        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cancel.StatusCode);
    }

    [Fact]
    public async Task Cancelling_one_recurring_occurrence_does_not_change_later_occurrences()
    {
        using var create = await Client.PostAsJsonAsync(
            "/api/recurring-jobs/daily",
            new
            {
                requestId = Guid.NewGuid(),
                childIds = new[] { DemoDataIds.Fredster },
                name = "Daily practice",
                description = "Ten minutes.",
                points = 2,
                agendaPeriod = "evening",
                scheduledTime = (string?)null,
                startDate = CurrentDate,
                endDate = CurrentDate.AddDays(2),
            });
        create.EnsureSuccessStatusCode();
        var today = await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Addie}");
        Assert.NotNull(today);
        var occurrence = today.Jobs.Single(job => job.Name == "Daily practice");

        using var cancel = await Client.PostAsJsonAsync(
            $"/api/jobs/{occurrence.Id}/cancel",
            new { reason = "Skip today." });
        cancel.EnsureSuccessStatusCode();

        var tomorrow = await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Addie}&date={CurrentDate.AddDays(1):yyyy-MM-dd}");
        Assert.NotNull(tomorrow);
        var laterOccurrence = Assert.Single(tomorrow.Jobs, job => job.Name == "Daily practice");
        Assert.Equal("open", laterOccurrence.Status);
        Assert.Equal(occurrence.RecurringJobSeriesId, laterOccurrence.RecurringJobSeriesId);
    }

    [Fact]
    public async Task Explicit_demo_seed_creates_not_set_credentials_for_every_profile()
    {
        await using var scope = (_factory
            ?? throw new InvalidOperationException("Test API was not initialised."))
            .Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(
            await database.HouseholdMembers.CountAsync(),
            await database.MemberCredentials.CountAsync());
        Assert.All(
            await database.MemberCredentials.ToListAsync(),
            credential => Assert.Equal(CredentialState.NotSet, credential.State));
    }

    [Fact]
    public async Task Adult_reset_atomically_clears_jobs_and_points_but_preserves_identity()
    {
        var client = Client;
        await CompleteAndApproveAsync(DemoDataIds.FeedDog);
        await CompleteAsync(DemoDataIds.PackBag);
        using (var reject = await client.PostAsJsonAsync(
            $"/api/jobs/{DemoDataIds.PackBag}/reject",
            new { reason = "Needs another try." }))
        {
            reject.EnsureSuccessStatusCode();
        }

        using (var recurring = await client.PostAsJsonAsync(
            "/api/recurring-jobs/daily",
            new
            {
                requestId = Guid.NewGuid(),
                childIds = new[] { DemoDataIds.Harrie },
                name = "Read together",
                description = "Ten minutes of reading.",
                points = 2,
                agendaPeriod = "evening",
                scheduledTime = (string?)null,
                startDate = CurrentDate,
                endDate = CurrentDate.AddDays(2),
            }))
        {
            Assert.Equal(HttpStatusCode.Created, recurring.StatusCode);
        }

        int jobCount;
        int seriesCount;
        int decisionCount;
        int pointsCount;
        MemberSnapshot[] memberSnapshots;
        CredentialSnapshot[] credentialSnapshots;
        await using (var scope = (_factory
            ?? throw new InvalidOperationException("Test API was not initialised."))
            .Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            jobCount = await database.Jobs.CountAsync();
            seriesCount = await database.RecurringJobSeries.CountAsync();
            decisionCount = await database.JobReviewDecisions.CountAsync();
            pointsCount = await database.PointsLedgerEntries.CountAsync();
            memberSnapshots = await database.HouseholdMembers
                .OrderBy(member => member.Id)
                .Select(member => new MemberSnapshot(
                    member.Id,
                    member.FirstName,
                    member.Surname,
                    member.Nickname,
                    member.Role,
                    member.IsActive))
                .ToArrayAsync();
            credentialSnapshots = await database.MemberCredentials
                .OrderBy(credential => credential.MemberId)
                .Select(credential => new CredentialSnapshot(
                    credential.MemberId,
                    credential.State,
                    credential.PinHash,
                    credential.PinSetAtUtc))
                .ToArrayAsync();
        }

        Assert.True(jobCount > 0);
        Assert.True(seriesCount > 0);
        Assert.True(decisionCount > 0);
        Assert.True(pointsCount > 0);

        using var resetResponse = await client.PostAsJsonAsync(
            "/api/admin/jobs-and-points/reset",
            new { confirmation = "RESET TASKS AND POINTS" });
        var reset = await resetResponse.Content.ReadFromJsonAsync<ResetJobsAndPointsResponse>();

        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
        Assert.NotNull(reset);
        Assert.Equal(jobCount, reset.DeletedJobCount);
        Assert.Equal(seriesCount, reset.DeletedRecurringSeriesCount);
        Assert.Equal(decisionCount, reset.DeletedReviewDecisionCount);
        Assert.Equal(pointsCount, reset.DeletedPointsEntryCount);

        await using (var scope = (_factory
            ?? throw new InvalidOperationException("Test API was not initialised."))
            .Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await database.Jobs.ToListAsync());
            Assert.Empty(await database.RecurringJobSeries.ToListAsync());
            Assert.Empty(await database.JobReviewDecisions.ToListAsync());
            Assert.Empty(await database.PointsLedgerEntries.ToListAsync());
            Assert.Equal(
                memberSnapshots,
                await database.HouseholdMembers
                    .OrderBy(member => member.Id)
                    .Select(member => new MemberSnapshot(
                        member.Id,
                        member.FirstName,
                        member.Surname,
                        member.Nickname,
                        member.Role,
                        member.IsActive))
                    .ToArrayAsync());
            Assert.Equal(
                credentialSnapshots,
                await database.MemberCredentials
                    .OrderBy(credential => credential.MemberId)
                    .Select(credential => new CredentialSnapshot(
                        credential.MemberId,
                        credential.State,
                        credential.PinHash,
                        credential.PinSetAtUtc))
                    .ToArrayAsync());
            var audit = Assert.Single(await database.HouseholdDataResets.ToListAsync());
            Assert.Equal(DemoDataIds.Addie, audit.InitiatedByAdultId);
            Assert.Equal(reset.ResetId, audit.Id);
        }

        var childBoard = await client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Fredster}");
        Assert.NotNull(childBoard);
        Assert.Empty(childBoard.Jobs);
        Assert.Equal(0, childBoard.PointsBalance);
        Assert.Empty(childBoard.PointEarnings);

        using var repeatedResponse = await client.PostAsJsonAsync(
            "/api/admin/jobs-and-points/reset",
            new { confirmation = "RESET TASKS AND POINTS" });
        var repeated = await repeatedResponse.Content
            .ReadFromJsonAsync<ResetJobsAndPointsResponse>();
        Assert.Equal(HttpStatusCode.OK, repeatedResponse.StatusCode);
        Assert.NotNull(repeated);
        Assert.Equal(0, repeated.DeletedJobCount);
        Assert.Equal(0, repeated.DeletedRecurringSeriesCount);
        Assert.Equal(0, repeated.DeletedReviewDecisionCount);
        Assert.Equal(0, repeated.DeletedPointsEntryCount);
    }

    [Fact]
    public async Task Reset_requires_exact_confirmation_and_an_adult()
    {
        await using var scope = (_factory
            ?? throw new InvalidOperationException("Test API was not initialised."))
            .Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var initialJobCount = await database.Jobs.CountAsync();

        using var unconfirmed = await Client.PostAsJsonAsync(
            "/api/admin/jobs-and-points/reset",
            new { confirmation = "reset" });
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);

        using var childRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/admin/jobs-and-points/reset")
        {
            Content = JsonContent.Create(new { confirmation = "RESET TASKS AND POINTS" }),
        };
        childRequest.Headers.Add("X-Test-Member-Id", DemoDataIds.Fredster.ToString());
        using var forbidden = await Client.SendAsync(childRequest);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        database.ChangeTracker.Clear();
        Assert.Equal(initialJobCount, await database.Jobs.CountAsync());
        Assert.Empty(await database.HouseholdDataResets.ToListAsync());
    }

    [Fact]
    public async Task Reset_rolls_back_every_delete_when_the_audit_write_fails()
    {
        var factory = _factory ?? throw new InvalidOperationException("Test API was not initialised.");
        int initialJobCount;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            initialJobCount = await database.Jobs.CountAsync();
            await database.Database.ExecuteSqlRawAsync(
                """
                CREATE FUNCTION fail_household_data_reset() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'forced audit failure';
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER fail_household_data_reset
                BEFORE INSERT ON household_data_resets
                FOR EACH ROW EXECUTE FUNCTION fail_household_data_reset();
                """);
        }

        using var response = await Client.PostAsJsonAsync(
            "/api/admin/jobs-and-points/reset",
            new { confirmation = "RESET TASKS AND POINTS" });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider
            .GetRequiredService<AppDbContext>();
        Assert.Equal(initialJobCount, await verificationDatabase.Jobs.CountAsync());
        Assert.Empty(await verificationDatabase.HouseholdDataResets.ToListAsync());
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
                $"UPDATE household_members SET nickname = {nickname} WHERE id = {DemoDataIds.Fredster}");
        }

        var board = await Client.GetFromJsonAsync<TodayResponse>("/api/today");

        Assert.NotNull(board);
        Assert.Equal("Fredster", board.Viewer.FirstName);
        Assert.Equal("Ads", board.Viewer.Nickname);
        Assert.Equal("Ads", board.Viewer.DisplayName);
    }

    [Fact]
    public async Task Adult_can_filter_a_daily_board_without_filtering_the_pending_count()
    {
        using var createResponse = await Client.PostAsJsonAsync(
            "/api/today/jobs",
            new
            {
                childIds = new[] { DemoDataIds.Harrie },
                name = "Filter target",
                description = "Visible only for Harrie.",
                points = 2,
                scheduledDate = CurrentDate,
                agendaPeriod = "morning",
                scheduledTime = "07:30:00",
            });
        var created = await createResponse.Content.ReadFromJsonAsync<AddJobsResponse>();
        createResponse.EnsureSuccessStatusCode();
        var target = Assert.Single(created!.Jobs);

        using var completeResponse = await Client.PostAsync(
            $"/api/jobs/{DemoDataIds.FeedDog}/complete",
            null);
        Assert.True(
            completeResponse.IsSuccessStatusCode
            || completeResponse.StatusCode == HttpStatusCode.Conflict);

        var household = await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Addie}");
        var filtered = await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Addie}&childId={DemoDataIds.Harrie}");

        Assert.NotNull(household);
        Assert.Null(household.SelectedChildId);
        Assert.NotNull(filtered);
        Assert.Equal(DemoDataIds.Harrie, filtered.SelectedChildId);
        Assert.NotEmpty(filtered.Jobs);
        Assert.All(filtered.Jobs, job => Assert.Equal(DemoDataIds.Harrie, job.ChildId));
        Assert.Contains(filtered.Jobs, job => job.Id == target.Id);
        Assert.Equal(household.PendingApprovalCount, filtered.PendingApprovalCount);
    }

    [Fact]
    public async Task Invalid_adult_filter_and_child_filter_are_rejected()
    {
        var inactiveChildId = Guid.NewGuid();
        await using (var scope = (_factory
            ?? throw new InvalidOperationException("Test API was not initialised."))
            .Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.HouseholdMembers.Add(new HouseholdMember(
                inactiveChildId,
                "Inactive filter child",
                false,
                isActive: false));
            await database.SaveChangesAsync();
        }

        foreach (var childId in new[] { Guid.NewGuid(), inactiveChildId, DemoDataIds.Addie })
        {
            using var invalid = await Client.GetAsync(
                $"/api/today?memberId={DemoDataIds.Addie}&childId={childId}");
            var problem = await invalid.Content.ReadFromJsonAsync<ValidationProblemDetails>();

            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("Invalid daily board filter", problem?.Title);
            Assert.Contains("ChildId", problem?.Errors.Keys ?? []);
        }

        using var childFilter = await Client.GetAsync(
            $"/api/today?childId={DemoDataIds.Fredster}");
        Assert.Equal(HttpStatusCode.Forbidden, childFilter.StatusCode);
    }

    [Fact]
    public async Task Profile_migration_preserves_existing_jobs_and_moves_them_to_Fredster()
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
        await migrator.MigrateAsync("20260831113745_AddHouseholdMemberNickname");
        await database.Database.ExecuteSqlRawAsync(
            "DROP TABLE IF EXISTS \"__family_jobs_board_fresh_install\";");
        await database.Database.ExecuteSqlRawAsync(
            "INSERT INTO household_members (id, first_name, nickname, is_adult) VALUES " +
            "('22eb0cc1-058e-4b2e-bb18-d7aaad564a6c', 'Addie', NULL, FALSE), " +
            "('9db319c1-28d1-4ce6-93d7-f04a45f8257d', 'Adam', NULL, TRUE);");
        var jobId = Guid.NewGuid();
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO jobs (id, child_id, name, description, points, scheduled_date, status, completed_at_utc, approved_at_utc) VALUES ({jobId}, {DemoDataIds.Addie}, 'Existing job', 'Keep me.', 3, DATE '2026-08-31', 'Open', NULL, NULL);");

        await migrator.MigrateAsync();
        database.ChangeTracker.Clear();
        var members = await database.HouseholdMembers.OrderBy(member => member.FirstName).ToListAsync();
        var job = await database.Jobs.SingleAsync(existing => existing.Id == jobId);

        Assert.Equal(["Addie", "Fredster", "Harrie", "Hellie"], members.Select(member => member.FirstName));
        Assert.True(members.Single(member => member.Id == DemoDataIds.Addie).IsAdult);
        Assert.True(members.Single(member => member.Id == DemoDataIds.Hellie).IsAdult);
        Assert.False(members.Single(member => member.Id == DemoDataIds.Fredster).IsAdult);
        Assert.False(members.Single(member => member.Id == DemoDataIds.Harrie).IsAdult);
        Assert.All(members, member => Assert.True(member.IsActive));
        Assert.Equal(DemoDataIds.Fredster, job.ChildId);
    }

    [Fact]
    public async Task Added_job_is_trimmed_listed_persisted_and_can_be_completed()
    {
        var client = Client;
        var scheduledDate = CurrentDate.AddDays(2);
        using var createResponse = await client.PostAsJsonAsync(
            "/api/today/jobs",
            new
            {
                childIds = new[] { DemoDataIds.Fredster },
                name = "  Put toys away  ",
                description = "  Return every toy to its box.  ",
                points = 4,
                scheduledDate,
                agendaPeriod = "arrivingHome",
                scheduledTime = "15:45:00",
            });
        var createdJobs = await createResponse.Content.ReadFromJsonAsync<AddJobsResponse>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = Assert.Single(createdJobs!.Jobs);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Put toys away", created.Name);
        Assert.Equal("Return every toy to its box.", created.Description);
        Assert.Equal(4, created.Points);
        Assert.Equal(scheduledDate, created.ScheduledDate);
        Assert.Equal("arrivingHome", created.AgendaPeriod);
        Assert.Equal(new TimeOnly(15, 45), created.ScheduledTime);
        Assert.Equal("open", created.Status);
        Assert.Null(created.CompletedAtUtc);

        var today = await client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.DoesNotContain(today!.Jobs, job => job.Id == created.Id);
        var listed = await client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?date={scheduledDate:yyyy-MM-dd}");
        Assert.Contains(listed!.Jobs, job => job.Id == created.Id);
        Assert.Equal(scheduledDate, listed.Date);
        Assert.Equal(CurrentDate, listed.CurrentDate);

        await RestartApplicationAsync();

        var persisted = await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?date={scheduledDate:yyyy-MM-dd}");
        Assert.Contains(persisted!.Jobs, job => job.Id == created.Id);

        using var completeResponse = await Client.PostAsync(
            $"/api/jobs/{created.Id}/complete",
            null);
        var completed = await completeResponse.Content.ReadFromJsonAsync<JobResponse>();

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        Assert.Equal("pendingApproval", completed!.Status);
    }

    [Fact]
    public async Task Job_for_both_children_creates_independent_copies()
    {
        using var createResponse = await Client.PostAsJsonAsync(
            "/api/today/jobs",
            new
            {
                childIds = new[] { DemoDataIds.Fredster, DemoDataIds.Harrie },
                name = "Make the beds",
                description = "Straighten the duvet and pillows.",
                points = 3,
                scheduledDate = CurrentDate,
                agendaPeriod = "unscheduled",
                scheduledTime = (string?)null,
            });
        var created = await createResponse.Content.ReadFromJsonAsync<AddJobsResponse>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);
        Assert.Equal(2, created.Jobs.Count);
        Assert.Equal(
            new[] { DemoDataIds.Fredster, DemoDataIds.Harrie }.Order(),
            created.Jobs.Select(job => job.ChildId).Order());
        Assert.Equal(2, created.Jobs.Select(job => job.Id).Distinct().Count());
        Assert.All(created.Jobs, job =>
        {
            Assert.Equal("Make the beds", job.Name);
            Assert.Equal("open", job.Status);
        });

        var fredsterJob = created.Jobs.Single(job => job.ChildId == DemoDataIds.Fredster);
        using var fredsterClient = (_factory
            ?? throw new InvalidOperationException("Test API was not initialised."))
            .CreateClient();
        fredsterClient.DefaultRequestHeaders.Add(
            "X-Test-Member-Id",
            DemoDataIds.Fredster.ToString());
        using var completeResponse = await fredsterClient.PostAsync(
            $"/api/jobs/{fredsterJob.Id}/complete",
            null);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        var adultBoard = await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Addie}");
        Assert.Equal(
            "pendingApproval",
            adultBoard!.Jobs.Single(job => job.Id == fredsterJob.Id).Status);
        var harrieJob = created.Jobs.Single(job => job.ChildId == DemoDataIds.Harrie);
        Assert.Equal(
            "open",
            adultBoard.Jobs.Single(job => job.Id == harrieJob.Id).Status);
    }

    [Fact]
    public async Task Invalid_assignee_set_persists_no_jobs()
    {
        await using (var scope = (_factory
            ?? throw new InvalidOperationException("Test API was not initialised."))
            .Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE household_members SET is_active = FALSE WHERE id = {DemoDataIds.Harrie}");
        }

        using var response = await Client.PostAsJsonAsync(
            "/api/today/jobs",
            new
            {
                childIds = new[] { DemoDataIds.Fredster, DemoDataIds.Harrie },
                name = "Do not create me",
                description = "The assignee set is invalid.",
                points = 1,
                scheduledDate = CurrentDate,
                agendaPeriod = "unscheduled",
                scheduledTime = (string?)null,
            });
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ChildIds", problem?.Errors.Keys ?? []);
        await using var verificationScope = (_factory
            ?? throw new InvalidOperationException("Test API was not initialised."))
            .Services.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await verificationDatabase.Jobs.AnyAsync(job => job.Name == "Do not create me"));
    }

    [Fact]
    public async Task Empty_and_duplicate_assignee_sets_are_rejected()
    {
        foreach (var childIds in new[]
                 {
                     Array.Empty<Guid>(),
                     new[] { DemoDataIds.Fredster, DemoDataIds.Fredster },
                 })
        {
            using var response = await Client.PostAsJsonAsync(
                "/api/today/jobs",
                new
                {
                    childIds,
                    name = "Invalid assignees",
                    description = "Do not persist this job.",
                    points = 1,
                    scheduledDate = CurrentDate,
                    agendaPeriod = "unscheduled",
                    scheduledTime = (string?)null,
                });
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("ChildIds", problem?.Errors.Keys ?? []);
        }

        await using var scope = (_factory
            ?? throw new InvalidOperationException("Test API was not initialised."))
            .Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await database.Jobs.AnyAsync(job => job.Name == "Invalid assignees"));
    }

    [Fact]
    public async Task Invalid_once_off_schedule_persists_no_jobs()
    {
        using var response = await Client.PostAsJsonAsync(
            "/api/today/jobs",
            new
            {
                childIds = new[] { DemoDataIds.Fredster },
                name = "Invalid schedule",
                description = "Do not persist this job.",
                points = 1,
                scheduledDate = CurrentDate.AddDays(-1),
                agendaPeriod = "bedtime",
                scheduledTime = (string?)null,
            });
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ScheduledDate", problem?.Errors.Keys ?? []);
        Assert.Contains("AgendaPeriod", problem?.Errors.Keys ?? []);

        using var invalidTimeResponse = await Client.PostAsJsonAsync(
            "/api/today/jobs",
            new
            {
                childIds = new[] { DemoDataIds.Fredster },
                name = "Invalid time",
                description = "Do not persist this job either.",
                points = 1,
                scheduledDate = CurrentDate,
                agendaPeriod = "morning",
                scheduledTime = "25:99:00",
            });

        Assert.Equal(HttpStatusCode.BadRequest, invalidTimeResponse.StatusCode);
        await using var scope = (_factory
            ?? throw new InvalidOperationException("Test API was not initialised."))
            .Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await database.Jobs.AnyAsync(job => job.Name == "Invalid schedule"));
        Assert.False(await database.Jobs.AnyAsync(job => job.Name == "Invalid time"));
    }

    [Fact]
    public async Task Child_cannot_complete_another_childs_job()
    {
        using var created = await Client.PostAsJsonAsync(
            "/api/today/jobs",
            new
            {
                childIds = new[] { DemoDataIds.Harrie },
                name = "Harrie's job",
                description = "Only Harrie can complete this.",
                points = 2,
                scheduledDate = CurrentDate,
                agendaPeriod = "unscheduled",
                scheduledTime = (string?)null,
            });
        var createdJobs = await created.Content.ReadFromJsonAsync<AddJobsResponse>();
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var job = Assert.Single(createdJobs!.Jobs);

        using var fredsterClient = (_factory
            ?? throw new InvalidOperationException("Test API was not initialised."))
            .CreateClient();
        fredsterClient.DefaultRequestHeaders.Add(
            "X-Test-Member-Id",
            DemoDataIds.Fredster.ToString());
        using var response = await fredsterClient.PostAsync(
            $"/api/jobs/{job.Id}/complete",
            null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Daily_recurring_job_is_materialized_once_persists_and_uses_the_job_workflow()
    {
        var initial = await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Addie}");
        Assert.NotNull(initial);
        var requestId = Guid.NewGuid();
        var endDate = initial.Date.AddDays(2);
        var request = new
        {
            requestId,
            viewerId = DemoDataIds.Addie,
            childIds = new[] { DemoDataIds.Fredster },
            name = "  Feed the fish  ",
            description = "  Add one small scoop.  ",
            points = 3,
            agendaPeriod = "morning",
            scheduledTime = "07:30:00",
            startDate = initial.Date,
            endDate,
        };

        using var createResponse = await Client.PostAsJsonAsync(
            "/api/recurring-jobs/daily",
            request);
        var created = await createResponse.Content.ReadFromJsonAsync<RecurringJobResponse>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);
        var assignment = Assert.Single(created.Assignments);
        Assert.Equal(DemoDataIds.Fredster, assignment.ChildId);
        Assert.Equal(endDate, assignment.GeneratedThrough);
        Assert.Equal(3, assignment.OccurrenceCount);

        var board = await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Fredster}");
        Assert.NotNull(board);
        var occurrence = Assert.Single(
            board.Jobs,
            job => job.RecurringJobSeriesId == assignment.SeriesId);
        Assert.Equal("Feed the fish", occurrence.Name);
        Assert.Equal("Add one small scoop.", occurrence.Description);
        Assert.Equal(initial.Date, occurrence.ScheduledDate);
        Assert.Equal("morning", occurrence.AgendaPeriod);
        Assert.Equal(new TimeOnly(7, 30), occurrence.ScheduledTime);
        Assert.Equal("daily", occurrence.RecurrenceFrequency);

        using var retryResponse = await Client.PostAsJsonAsync(
            "/api/recurring-jobs/daily",
            request);
        var retried = await retryResponse.Content.ReadFromJsonAsync<RecurringJobResponse>();
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.Equal(3, Assert.Single(retried!.Assignments).OccurrenceCount);

        var factory = _factory ?? throw new InvalidOperationException("Test API was not initialised.");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(
                1,
                await database.RecurringJobSeries.CountAsync(
                    series => series.AssignmentRequestId == requestId));
            Assert.Equal(
                3,
                await database.Jobs.CountAsync(
                    job => job.RecurringJobSeriesId == assignment.SeriesId));
        }

        await RestartApplicationAsync();

        var persisted = await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Fredster}");
        var persistedOccurrence = Assert.Single(
            persisted!.Jobs,
            job => job.RecurringJobSeriesId == assignment.SeriesId);
        using var completeResponse = await Client.PostAsync(
            $"/api/jobs/{persistedOccurrence.Id}/complete",
            null);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        using var approveResponse = await Client.PostAsync(
            $"/api/jobs/{persistedOccurrence.Id}/approve",
            null);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var awarded = await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Fredster}");
        Assert.Contains(awarded!.PointEarnings, earning => earning.JobId == persistedOccurrence.Id);
    }

    [Theory]
    [InlineData("daily")]
    [InlineData("weekly")]
    [InlineData("monthly")]
    public async Task Recurring_schedule_for_both_children_is_atomic_and_idempotent(
        string frequency)
    {
        var initial = await Client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.NotNull(initial);
        var requestId = Guid.NewGuid();
        var request = new
        {
            requestId,
            childIds = new[] { DemoDataIds.Fredster, DemoDataIds.Harrie },
            name = $"Both children {frequency}",
            description = "Each child gets an independent copy.",
            points = 4,
            agendaPeriod = "morning",
            scheduledTime = (string?)null,
            startDate = initial.Date,
            endDate = initial.Date,
            weekdays = new[] { initial.Date.DayOfWeek.ToString().ToLowerInvariant() },
            dayOfMonth = initial.Date.Day,
        };

        using var createResponse = await Client.PostAsJsonAsync(
            $"/api/recurring-jobs/{frequency}",
            request);
        var created = await createResponse.Content.ReadFromJsonAsync<RecurringJobResponse>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);
        Assert.Equal(2, created.Assignments.Count);
        Assert.Equal(
            new[] { DemoDataIds.Fredster, DemoDataIds.Harrie }.Order(),
            created.Assignments.Select(assignment => assignment.ChildId).Order());
        Assert.Equal(2, created.Assignments.Select(assignment => assignment.SeriesId).Distinct().Count());
        Assert.All(created.Assignments, assignment => Assert.Equal(1, assignment.OccurrenceCount));

        using var retryResponse = await Client.PostAsJsonAsync(
            $"/api/recurring-jobs/{frequency}",
            request);
        var retried = await retryResponse.Content.ReadFromJsonAsync<RecurringJobResponse>();
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.Equal(
            created.Assignments.Select(assignment => assignment.SeriesId).Order(),
            retried!.Assignments.Select(assignment => assignment.SeriesId).Order());

        await using var scope = (_factory
            ?? throw new InvalidOperationException("Test API was not initialised."))
            .Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var series = await database.RecurringJobSeries
            .Where(item => item.AssignmentRequestId == requestId)
            .ToListAsync();
        Assert.Equal(2, series.Count);
        var seriesIds = series.Select(item => item.Id).ToArray();
        var occurrenceCounts = await database.Jobs
            .Where(job => job.RecurringJobSeriesId != null
                && seriesIds.Contains(job.RecurringJobSeriesId.Value))
            .GroupBy(job => job.RecurringJobSeriesId!.Value)
            .ToDictionaryAsync(group => group.Key, group => group.Count());
        Assert.All(seriesIds, seriesId => Assert.Equal(1, occurrenceCounts[seriesId]));
    }

    [Fact]
    public async Task Invalid_recurring_assignee_set_persists_no_series_or_occurrences()
    {
        var initial = await Client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.NotNull(initial);
        var requestId = Guid.NewGuid();

        using var response = await Client.PostAsJsonAsync(
            "/api/recurring-jobs/daily",
            new
            {
                requestId,
                childIds = new[] { DemoDataIds.Fredster, DemoDataIds.Addie },
                name = "Do not schedule me",
                description = "One assignee is an adult.",
                points = 2,
                agendaPeriod = "morning",
                scheduledTime = (string?)null,
                startDate = initial.Date,
                endDate = initial.Date,
            });
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ChildIds", problem?.Errors.Keys ?? []);
        await using var scope = (_factory
            ?? throw new InvalidOperationException("Test API was not initialised."))
            .Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await database.RecurringJobSeries.AnyAsync(
            series => series.AssignmentRequestId == requestId));
        Assert.False(await database.Jobs.AnyAsync(job => job.Name == "Do not schedule me"));
    }

    [Fact]
    public async Task Weekly_recurring_job_materializes_selected_weekdays_once_and_survives_restart()
    {
        var initial = await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Addie}");
        Assert.NotNull(initial);
        var requestId = Guid.NewGuid();
        var endDate = initial.Date.AddDays(8);
        var selectedWeekdays = new[]
        {
            initial.Date.DayOfWeek,
            initial.Date.AddDays(2).DayOfWeek,
        };
        var expectedDates = Enumerable.Range(0, 9)
            .Select(offset => initial.Date.AddDays(offset))
            .Where(date => selectedWeekdays.Contains(date.DayOfWeek))
            .ToArray();
        var request = new
        {
            requestId,
            viewerId = DemoDataIds.Addie,
            childIds = new[] { DemoDataIds.Fredster },
            name = "  Pack sports kit  ",
            description = "  Check the kit bag.  ",
            points = 4,
            agendaPeriod = "evening",
            scheduledTime = "18:15:00",
            startDate = initial.Date,
            endDate,
            weekdays = selectedWeekdays
                .Select(day => day.ToString().ToLowerInvariant())
                .ToArray(),
        };

        using var createResponse = await Client.PostAsJsonAsync(
            "/api/recurring-jobs/weekly",
            request);
        var created = await createResponse.Content.ReadFromJsonAsync<RecurringJobResponse>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);
        var assignment = Assert.Single(created.Assignments);
        Assert.Equal(DemoDataIds.Fredster, assignment.ChildId);
        Assert.Equal(endDate, assignment.GeneratedThrough);
        Assert.Equal(expectedDates.Length, assignment.OccurrenceCount);

        var board = await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Fredster}");
        var occurrence = Assert.Single(
            board!.Jobs,
            job => job.RecurringJobSeriesId == assignment.SeriesId);
        Assert.Equal("Pack sports kit", occurrence.Name);
        Assert.Equal(initial.Date, occurrence.ScheduledDate);
        Assert.Equal("weekly", occurrence.RecurrenceFrequency);
        Assert.Equal("evening", occurrence.AgendaPeriod);
        Assert.Equal(new TimeOnly(18, 15), occurrence.ScheduledTime);

        using var retryResponse = await Client.PostAsJsonAsync(
            "/api/recurring-jobs/weekly",
            request);
        var retried = await retryResponse.Content.ReadFromJsonAsync<RecurringJobResponse>();
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.Equal(
            expectedDates.Length,
            Assert.Single(retried!.Assignments).OccurrenceCount);

        await RestartApplicationAsync();
        await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Fredster}");

        var factory = _factory ?? throw new InvalidOperationException("Test API was not initialised.");
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var series = await database.RecurringJobSeries.SingleAsync(
            item => item.AssignmentRequestId == requestId);
        var persistedDates = await database.Jobs
            .Where(job => job.RecurringJobSeriesId == assignment.SeriesId)
            .OrderBy(job => job.ScheduledDate)
            .Select(job => job.ScheduledDate)
            .ToArrayAsync();
        Assert.Equal(RecurrenceFrequency.Weekly, series.Frequency);
        Assert.Equal(selectedWeekdays.Order(), series.SelectedWeekdays().Order());
        Assert.Equal(expectedDates, persistedDates);
    }

    [Fact]
    public async Task Monthly_recurring_job_materializes_once_and_survives_restart()
    {
        var initial = await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Addie}");
        Assert.NotNull(initial);
        var requestId = Guid.NewGuid();
        var secondMonth = initial.Date.AddMonths(1);
        var expectedDates = new[]
        {
            initial.Date,
            new DateOnly(
                secondMonth.Year,
                secondMonth.Month,
                Math.Min(initial.Date.Day, DateTime.DaysInMonth(secondMonth.Year, secondMonth.Month))),
        };
        var request = new
        {
            requestId,
            viewerId = DemoDataIds.Addie,
            childIds = new[] { DemoDataIds.Fredster },
            name = "  Clean the fridge  ",
            description = "  Check every shelf.  ",
            points = 5,
            agendaPeriod = "morning",
            scheduledTime = "09:15:00",
            startDate = initial.Date,
            endDate = expectedDates[^1],
            dayOfMonth = initial.Date.Day,
        };

        using var createResponse = await Client.PostAsJsonAsync(
            "/api/recurring-jobs/monthly",
            request);
        var created = await createResponse.Content.ReadFromJsonAsync<RecurringJobResponse>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);
        var assignment = Assert.Single(created.Assignments);
        Assert.Equal(DemoDataIds.Fredster, assignment.ChildId);
        Assert.Equal(expectedDates[^1], assignment.GeneratedThrough);
        Assert.Equal(expectedDates.Length, assignment.OccurrenceCount);

        var board = await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Fredster}");
        var occurrence = Assert.Single(
            board!.Jobs,
            job => job.RecurringJobSeriesId == assignment.SeriesId);
        Assert.Equal("Clean the fridge", occurrence.Name);
        Assert.Equal(initial.Date, occurrence.ScheduledDate);
        Assert.Equal("monthly", occurrence.RecurrenceFrequency);
        Assert.Equal("morning", occurrence.AgendaPeriod);
        Assert.Equal(new TimeOnly(9, 15), occurrence.ScheduledTime);

        using var retryResponse = await Client.PostAsJsonAsync(
            "/api/recurring-jobs/monthly",
            request);
        var retried = await retryResponse.Content.ReadFromJsonAsync<RecurringJobResponse>();
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.Equal(
            expectedDates.Length,
            Assert.Single(retried!.Assignments).OccurrenceCount);

        await RestartApplicationAsync();
        await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Fredster}");

        var factory = _factory ?? throw new InvalidOperationException("Test API was not initialised.");
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var series = await database.RecurringJobSeries.SingleAsync(
            item => item.AssignmentRequestId == requestId);
        var persistedDates = await database.Jobs
            .Where(job => job.RecurringJobSeriesId == assignment.SeriesId)
            .OrderBy(job => job.ScheduledDate)
            .Select(job => job.ScheduledDate)
            .ToArrayAsync();
        Assert.Equal(RecurrenceFrequency.Monthly, series.Frequency);
        Assert.Equal(initial.Date.Day, series.MonthlyDay);
        Assert.Equal(expectedDates, persistedDates);
    }

    [Fact]
    public async Task Monthly_migration_preserves_existing_recurring_jobs_and_refuses_a_lossy_downgrade()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("family_jobs_board_monthly_upgrade_tests")
            .WithUsername("family_jobs_board")
            .WithPassword("family_jobs_board")
            .Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new AppDbContext(options);
        var migrator = database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260902070811_AddWeeklyRecurringJobs");

        var dailySeriesId = Guid.NewGuid();
        var weeklySeriesId = Guid.NewGuid();
        var dailyJobId = Guid.NewGuid();
        var weeklyJobId = Guid.NewGuid();
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO household_members (id, first_name, nickname, is_adult) VALUES ({DemoDataIds.Hellie}, 'Hellie', NULL, TRUE), ({DemoDataIds.Fredster}, 'Fredster', NULL, FALSE) ON CONFLICT (id) DO NOTHING;");
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO recurring_job_series (id, child_id, created_by_adult_id, name, description, points, agenda_period, scheduled_time, start_date, end_date, generated_through, frequency, weekday_mask) VALUES ({dailySeriesId}, {DemoDataIds.Fredster}, {DemoDataIds.Hellie}, 'Daily job', 'Keep daily.', 2, 'Unscheduled', NULL, DATE '2026-09-02', NULL, DATE '2026-09-02', 'Daily', 0), ({weeklySeriesId}, {DemoDataIds.Fredster}, {DemoDataIds.Hellie}, 'Weekly job', 'Keep weekly.', 3, 'Evening', TIME '18:00', DATE '2026-09-02', NULL, DATE '2026-09-02', 'Weekly', 4);");
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO jobs (id, child_id, name, description, points, scheduled_date, status, completed_at_utc, approved_at_utc, recurring_job_series_id, agenda_period, scheduled_time, recurrence_frequency) VALUES ({dailyJobId}, {DemoDataIds.Fredster}, 'Daily job', 'Keep daily.', 2, DATE '2026-09-02', 'Open', NULL, NULL, {dailySeriesId}, 'Unscheduled', NULL, 'Daily'), ({weeklyJobId}, {DemoDataIds.Fredster}, 'Weekly job', 'Keep weekly.', 3, DATE '2026-09-02', 'Open', NULL, NULL, {weeklySeriesId}, 'Evening', TIME '18:00', 'Weekly');");

        await migrator.MigrateAsync();
        database.ChangeTracker.Clear();

        var preservedSeries = await database.RecurringJobSeries
            .Where(series => series.Id == dailySeriesId || series.Id == weeklySeriesId)
            .OrderBy(series => series.Name)
            .ToListAsync();
        var preservedJobIds = await database.Jobs
            .Where(job => job.Id == dailyJobId || job.Id == weeklyJobId)
            .Select(job => job.Id)
            .ToListAsync();

        Assert.Equal(2, preservedSeries.Count);
        Assert.Equal(
            [RecurrenceFrequency.Daily, RecurrenceFrequency.Weekly],
            preservedSeries.Select(series => series.Frequency));
        Assert.All(preservedSeries, series => Assert.Null(series.MonthlyDay));
        Assert.All(preservedSeries, series => Assert.Equal(series.Id, series.AssignmentRequestId));
        Assert.Equal(new[] { dailyJobId, weeklyJobId }.Order(), preservedJobIds.Order());

        var monthlySeries = RecurringJobSeries.Monthly(
            Guid.NewGuid(),
            DemoDataIds.Fredster,
            DemoDataIds.Hellie,
            "Monthly job",
            "Prevent a lossy downgrade.",
            4,
            AgendaPeriod.Unscheduled,
            null,
            new DateOnly(2026, 9, 2),
            null,
            2);
        database.RecurringJobSeries.Add(monthlySeries);
        await database.SaveChangesAsync();

        var downgradeError = await Assert.ThrowsAsync<PostgresException>(
            () => migrator.MigrateAsync("20260902070811_AddWeeklyRecurringJobs"));
        Assert.Equal(PostgresErrorCodes.RaiseException, downgradeError.SqlState);
        Assert.Contains("Cannot downgrade while monthly recurring jobs exist", downgradeError.MessageText);
    }

    [Fact]
    public async Task Weekly_recurring_job_rejects_missing_duplicate_and_unknown_weekdays()
    {
        var initial = await Client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.NotNull(initial);

        foreach (var weekdays in new[]
                 {
                     Array.Empty<string>(),
                     ["monday", "monday"],
                     ["monday", "funday"],
                 })
        {
            using var response = await Client.PostAsJsonAsync(
                "/api/recurring-jobs/weekly",
                new
                {
                    requestId = Guid.NewGuid(),
                    viewerId = DemoDataIds.Addie,
                    childIds = new[] { DemoDataIds.Fredster },
                    name = "Pack sports kit",
                    description = "Check the kit bag.",
                    points = 4,
                    agendaPeriod = "evening",
                    scheduledTime = (string?)null,
                    startDate = initial.Date,
                    endDate = (DateOnly?)null,
                    weekdays,
                });
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Invalid weekly recurring job data", problem?.Title);
            Assert.Contains("Weekdays", problem?.Errors.Keys ?? []);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public async Task Monthly_recurring_job_rejects_an_invalid_day(int dayOfMonth)
    {
        var initial = await Client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.NotNull(initial);

        using var response = await Client.PostAsJsonAsync(
            "/api/recurring-jobs/monthly",
            new
            {
                requestId = Guid.NewGuid(),
                viewerId = DemoDataIds.Addie,
                childIds = new[] { DemoDataIds.Fredster },
                name = "Clean the fridge",
                description = "Check every shelf.",
                points = 5,
                agendaPeriod = "morning",
                scheduledTime = (string?)null,
                startDate = initial.Date,
                endDate = (DateOnly?)null,
                dayOfMonth,
            });
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Invalid monthly recurring job data", problem?.Title);
        Assert.Contains("DayOfMonth", problem?.Errors.Keys ?? []);
    }

    [Fact]
    public async Task Daily_recurring_job_requires_an_adult_valid_child_and_valid_values()
    {
        var initial = await Client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.NotNull(initial);

        using var childClient = (_factory
            ?? throw new InvalidOperationException("Test API was not initialised."))
            .CreateClient();
        childClient.DefaultRequestHeaders.Add("X-Test-Member-Id", DemoDataIds.Fredster.ToString());
        using var childViewer = await childClient.PostAsJsonAsync(
            "/api/recurring-jobs/daily",
            new
            {
                requestId = Guid.NewGuid(),
                viewerId = DemoDataIds.Fredster,
                childIds = new[] { DemoDataIds.Fredster },
                name = "Feed the fish",
                description = "One scoop.",
                points = 2,
                agendaPeriod = "morning",
                scheduledTime = (string?)null,
                startDate = initial.Date,
                endDate = (DateOnly?)null,
            });
        Assert.Equal(HttpStatusCode.Forbidden, childViewer.StatusCode);

        using var invalid = await Client.PostAsJsonAsync(
            "/api/recurring-jobs/daily",
            new
            {
                requestId = Guid.NewGuid(),
                viewerId = DemoDataIds.Addie,
                childIds = new[] { Guid.NewGuid() },
                name = "Feed the fish",
                description = "One scoop.",
                points = -1,
                agendaPeriod = "bedtime",
                scheduledTime = (string?)null,
                startDate = initial.Date,
                endDate = initial.Date.AddDays(-1),
            });
        var invalidProblem = await invalid.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.NotNull(invalidProblem);
        Assert.Equal("Invalid daily recurring job data", invalidProblem.Title);
        Assert.Contains("ChildIds", invalidProblem.Errors.Keys);
        Assert.Contains("Points", invalidProblem.Errors.Keys);
        Assert.Contains("AgendaPeriod", invalidProblem.Errors.Keys);
        Assert.Contains("EndDate", invalidProblem.Errors.Keys);
    }

    [Fact]
    public async Task Reusing_a_daily_request_id_with_different_details_is_rejected()
    {
        var initial = await Client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.NotNull(initial);
        var requestId = Guid.NewGuid();

        using var created = await Client.PostAsJsonAsync(
            "/api/recurring-jobs/daily",
            new
            {
                requestId,
                viewerId = DemoDataIds.Addie,
                childIds = new[] { DemoDataIds.Fredster },
                name = "Feed the fish",
                description = "One scoop.",
                points = 2,
                agendaPeriod = "morning",
                scheduledTime = (string?)null,
                startDate = initial.Date,
                endDate = initial.Date,
            });
        created.EnsureSuccessStatusCode();

        using var conflict = await Client.PostAsJsonAsync(
            "/api/recurring-jobs/daily",
            new
            {
                requestId,
                viewerId = DemoDataIds.Addie,
                childIds = new[] { DemoDataIds.Fredster },
                name = "Different job",
                description = "One scoop.",
                points = 2,
                agendaPeriod = "morning",
                scheduledTime = (string?)null,
                startDate = initial.Date,
                endDate = initial.Date,
            });

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Selected_member_gets_a_role_appropriate_board()
    {
        using var addForHarrie = await Client.PostAsJsonAsync(
            "/api/today/jobs",
            new
            {
                childIds = new[] { DemoDataIds.Harrie },
                name = "Water the plants",
                description = "Give each plant a small drink.",
                points = 2,
                scheduledDate = CurrentDate,
                agendaPeriod = "unscheduled",
                scheduledTime = (string?)null,
            });
        addForHarrie.EnsureSuccessStatusCode();

        var adult = await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Addie}");
        var fredster = await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Fredster}");
        var harrie = await Client.GetFromJsonAsync<TodayResponse>(
            $"/api/today?memberId={DemoDataIds.Harrie}");

        Assert.NotNull(adult);
        Assert.True(adult.Viewer.IsAdult);
        Assert.Null(adult.PointsBalance);
        Assert.Empty(adult.PointEarnings);
        Assert.Contains(adult.Jobs, job => job.ChildId == DemoDataIds.Fredster);
        Assert.Contains(adult.Jobs, job => job.ChildId == DemoDataIds.Harrie);

        Assert.NotNull(fredster);
        Assert.False(fredster.Viewer.IsAdult);
        Assert.All(fredster.Jobs, job => Assert.Equal(DemoDataIds.Fredster, job.ChildId));

        Assert.NotNull(harrie);
        var harrieJob = Assert.Single(harrie.Jobs);
        Assert.Equal("Water the plants", harrieJob.Name);
        Assert.Equal("Harrie", harrieJob.ChildDisplayName);
    }

    [Fact]
    public async Task Unknown_viewer_and_non_child_assignee_return_problem_details()
    {
        using var unknownViewer = await Client.GetAsync(
            $"/api/today?memberId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, unknownViewer.StatusCode);
        Assert.Equal(
            "Household member not found",
            (await unknownViewer.Content.ReadFromJsonAsync<ProblemDetails>())?.Title);

        using var adultAssignee = await Client.PostAsJsonAsync(
            "/api/today/jobs",
            new
            {
                childIds = new[] { DemoDataIds.Addie },
                name = "Invalid assignment",
                description = "Adults cannot be job assignees.",
                points = 1,
                scheduledDate = CurrentDate,
                agendaPeriod = "unscheduled",
                scheduledTime = (string?)null,
            });
        var problem = await adultAssignee.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Equal(HttpStatusCode.BadRequest, adultAssignee.StatusCode);
        Assert.Contains("ChildIds", problem?.Errors.Keys ?? []);
    }

    [Fact]
    public async Task Pending_job_can_be_approved_once_and_award_persists_after_restart()
    {
        var initial = await Client.GetFromJsonAsync<TodayResponse>("/api/today");
        Assert.NotNull(initial);
        Assert.Equal(0, initial.PointsBalance);
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
        Assert.Equal(target.Points, persisted.PointsBalance);
        var earning = Assert.Single(persisted.PointEarnings);
        Assert.NotEqual(Guid.Empty, earning.Id);
        Assert.Equal(target.Id, earning.JobId);
        Assert.Equal(target.Name, earning.Name);
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
        Assert.Equal(0, persisted.PointsBalance);
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
        Assert.Equal(target.Points, finalBoard.PointsBalance);
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
        Assert.Equal(0, board?.PointsBalance);
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
        await database.Database.ExecuteSqlRawAsync(
            "DROP TABLE IF EXISTS \"__family_jobs_board_fresh_install\";");
        var jobId = Guid.NewGuid();
        var approvedAtUtc = new DateTimeOffset(2026, 8, 31, 10, 30, 0, TimeSpan.Zero);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO household_members (id, first_name, nickname, is_adult) VALUES ({DemoDataIds.Addie}, 'Addie', NULL, FALSE);");
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO jobs (id, child_id, name, description, points, scheduled_date, status, completed_at_utc, approved_at_utc) VALUES ({jobId}, {DemoDataIds.Addie}, 'Existing job', 'Already approved.', 4, DATE '2026-08-31', 'Approved', {approvedAtUtc}, {approvedAtUtc});");

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
        Assert.Equal(first.Points + second.Points, board.PointsBalance);
        Assert.Equal(board.PointsBalance, board.PointEarnings.Sum(earning => earning.Points));
        Assert.Collection(
            board.PointEarnings,
            earning =>
            {
                Assert.Equal(second.Id, earning.JobId);
                Assert.Equal(second.Name, earning.Name);
            },
            earning =>
            {
                Assert.Equal(first.Id, earning.JobId);
                Assert.Equal(first.Name, earning.Name);
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
        Assert.Equal(5, board.PointsBalance);
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
            new
            {
                childIds = new[] { DemoDataIds.Fredster },
                name,
                description,
                points,
                scheduledDate = CurrentDate,
                agendaPeriod = "unscheduled",
                scheduledTime = (string?)null,
            });
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

    private DateOnly CurrentDate => (_factory
        ?? throw new InvalidOperationException("Test API was not initialised."))
        .Services.GetRequiredService<IHouseholdClock>()
        .Today;

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

    internal sealed record TodayResponse(
        MemberResponse Viewer,
        IReadOnlyList<MemberResponse> Members,
        DateOnly Date,
        DateOnly CurrentDate,
        Guid? SelectedChildId,
        IReadOnlyList<JobResponse> Jobs,
        int? PointsBalance,
        IReadOnlyList<PointEarningResponse> PointEarnings,
        int PendingApprovalCount);

    internal sealed record MemberResponse(
        Guid Id,
        string FirstName,
        string? Nickname,
        string DisplayName,
        bool IsAdult);

    internal sealed record JobResponse(
        Guid Id,
        Guid ChildId,
        string ChildDisplayName,
        string Name,
        string Description,
        int Points,
        DateOnly ScheduledDate,
        string AgendaPeriod,
        TimeOnly? ScheduledTime,
        Guid? RecurringJobSeriesId,
        string? RecurrenceFrequency,
        string Status,
        DateTimeOffset? CompletedAtUtc,
        DateTimeOffset? ApprovedAtUtc,
        JobRejectionResponse? LatestRejection);

    internal sealed record JobRejectionResponse(
        Guid DecisionId,
        string? Reason,
        DateTimeOffset RejectedAtUtc);

    private sealed record JobApprovalResponse(JobResponse Job, int PointsBalance);

    private sealed record AddJobsResponse(IReadOnlyList<JobResponse> Jobs);

    private sealed record RecurringJobResponse(
        IReadOnlyList<RecurringJobAssignmentResponse> Assignments);

    private sealed record RecurringJobAssignmentResponse(
        Guid SeriesId,
        Guid ChildId,
        DateOnly GeneratedThrough,
        int OccurrenceCount);

    internal sealed record PointEarningResponse(
        Guid Id,
        string Source,
        string Name,
        Guid? JobId,
        int Points,
        DateTimeOffset AwardedAtUtc,
        string? LoggedByDisplayName);

    private sealed record ResetJobsAndPointsResponse(
        Guid ResetId,
        DateTimeOffset OccurredAtUtc,
        int DeletedJobCount,
        int DeletedRecurringSeriesCount,
        int DeletedReviewDecisionCount,
        int DeletedPointsEntryCount);

    private sealed record MemberSnapshot(
        Guid Id,
        string FirstName,
        string? Surname,
        string? Nickname,
        HouseholdRole Role,
        bool IsActive);

    private sealed record CredentialSnapshot(
        Guid MemberId,
        CredentialState State,
        string? PinHash,
        DateTimeOffset? PinSetAtUtc);

    internal sealed class TestApiFactory : WebApplicationFactory<Program>
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
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });
            });
        }
    }
}
