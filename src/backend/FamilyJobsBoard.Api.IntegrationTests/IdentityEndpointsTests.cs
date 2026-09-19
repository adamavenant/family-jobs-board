using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyJobsBoard.Api.Composition;
using FamilyJobsBoard.Api.Features.Identity;
using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Identity;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace FamilyJobsBoard.Api.IntegrationTests;

public sealed class IdentityEndpointsTests : IAsyncLifetime
{
    private const string Origin = "http://localhost:3000";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("family_jobs_board_identity_tests")
        .WithUsername("family_jobs_board")
        .WithPassword("family_jobs_board")
        .Build();

    private IdentityApiFactory? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new IdentityApiFactory(_postgres.GetConnectionString());
        _client = CreateClient(_factory);
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
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
    public async Task Fresh_household_bootstraps_an_adult_and_enforces_logout()
    {
        Assert.Equal(
            Origin,
            Factory.Services.GetRequiredService<FamilyAuthenticationOptions>().AllowedOrigin);
        var start = await Client.GetFromJsonAsync<AuthStart>("/api/auth/start");
        Assert.Equal("createFirstAdult", start?.State);
        using var anonymousToday = await Client.GetAsync("/api/today");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousToday.StatusCode);

        var bootstrap = await BootstrapAdultAsync("012345");
        Assert.Equal(HttpStatusCode.Created, bootstrap.Response.StatusCode);
        Assert.Equal("adult", bootstrap.Body.Member.Role);

        using var protectedToday = await SendAuthorizedAsync(
            HttpMethod.Get,
            "/api/today",
            bootstrap.Body.AccessToken);
        Assert.Equal(HttpStatusCode.OK, protectedToday.StatusCode);

        using var logout = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/auth/logout",
            bootstrap.Body.AccessToken,
            includeOrigin: true);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var revokedToday = await SendAuthorizedAsync(
            HttpMethod.Get,
            "/api/today",
            bootstrap.Body.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedToday.StatusCode);

        using var signIn = await Client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { memberId = bootstrap.Body.Member.Id, pin = "012345" });
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/today")]
    [InlineData("POST", "/api/today/jobs")]
    [InlineData("POST", "/api/jobs/7009b529-733c-4770-ae56-1f6fa69f6363/complete")]
    [InlineData("POST", "/api/jobs/7009b529-733c-4770-ae56-1f6fa69f6363/approve")]
    [InlineData("POST", "/api/jobs/7009b529-733c-4770-ae56-1f6fa69f6363/reject")]
    [InlineData("POST", "/api/recurring-jobs/daily")]
    [InlineData("POST", "/api/recurring-jobs/weekly")]
    [InlineData("POST", "/api/recurring-jobs/monthly")]
    [InlineData("POST", "/api/admin/jobs-and-points/reset")]
    [InlineData("GET", "/api/users")]
    [InlineData("POST", "/api/users")]
    [InlineData("PATCH", "/api/users/7009b529-733c-4770-ae56-1f6fa69f6363")]
    [InlineData("DELETE", "/api/users/7009b529-733c-4770-ae56-1f6fa69f6363")]
    [InlineData("POST", "/api/users/7009b529-733c-4770-ae56-1f6fa69f6363/restore")]
    [InlineData("POST", "/api/users/7009b529-733c-4770-ae56-1f6fa69f6363/pin-setup")]
    [InlineData("POST", "/api/users/7009b529-733c-4770-ae56-1f6fa69f6363/pin-reset")]
    public async Task Application_endpoints_reject_anonymous_requests(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { });
        }

        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Concurrent_bootstrap_has_exactly_one_winner()
    {
        var requests = new[]
        {
            Client.PostAsJsonAsync(
                "/api/auth/bootstrap",
                new { mode = "createFirstAdult", firstName = "Addie", surname = "Avenant", pin = "012345" }),
            Client.PostAsJsonAsync(
                "/api/auth/bootstrap",
                new { mode = "createFirstAdult", firstName = "Hellie", surname = "Avenant", pin = "654321" }),
        };

        var responses = await Task.WhenAll(requests);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await database.HouseholdMembers.CountAsync());
        Assert.Equal(1, await database.MemberCredentials.CountAsync(
            credential => credential.State == CredentialState.Ready));
    }

    [Fact]
    public async Task Five_failed_PINs_lock_the_profile_without_leaking_detail()
    {
        var bootstrap = await BootstrapAdultAsync("123456");
        await LogoutAsync(bootstrap.Body);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var rejected = await Client.PostAsJsonAsync(
                "/api/auth/sign-in",
                new { memberId = bootstrap.Body.Member.Id, pin = "000000" });
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            Assert.Equal("invalid_credentials", await ProblemCodeAsync(rejected));
        }

        using var locked = await Client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { memberId = bootstrap.Body.Member.Id, pin = "000000" });
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal("try_again_later", await ProblemCodeAsync(locked));
        Assert.True(locked.Headers.RetryAfter?.Delta > TimeSpan.Zero);

        using var correctButLocked = await Client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { memberId = bootstrap.Body.Member.Id, pin = "123456" });
        Assert.Equal(HttpStatusCode.TooManyRequests, correctButLocked.StatusCode);
    }

    [Fact]
    public async Task Malformed_PINs_do_not_increment_the_profile_lock_counter()
    {
        var bootstrap = await BootstrapAdultAsync("123456");
        await LogoutAsync(bootstrap.Body);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var rejected = await Client.PostAsJsonAsync(
                "/api/auth/sign-in",
                new { memberId = bootstrap.Body.Member.Id, pin = "123" });
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }

        using var accepted = await Client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { memberId = bootstrap.Body.Member.Id, pin = "123456" });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_and_replay_revokes_the_session()
    {
        var bootstrap = await BootstrapAdultAsync("123456");
        var oldRefresh = RefreshCookie(bootstrap.Response);
        using var refreshed = await RefreshAsync(oldRefresh);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var nextRefresh = RefreshCookie(refreshed);
        Assert.NotEqual(oldRefresh, nextRefresh);

        using var replay = await RefreshAsync(oldRefresh);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal("session_expired", await ProblemCodeAsync(replay));

        using var revoked = await RefreshAsync(nextRefresh);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task Session_survives_restart_but_expires_at_ten_minutes_idle()
    {
        var bootstrap = await BootstrapAdultAsync("123456");
        var refresh = RefreshCookie(bootstrap.Response);

        await RestartApplicationAsync();
        Factory.Clock.Advance(TimeSpan.FromMinutes(9));
        using var renewed = await RefreshAsync(refresh);
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        var rotated = RefreshCookie(renewed);

        Factory.Clock.Advance(TimeSpan.FromMinutes(10));
        using var expired = await RefreshAsync(rotated);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal("session_expired", await ProblemCodeAsync(expired));
    }

    [Fact]
    public async Task Adult_handoff_sets_missing_surname_revokes_adult_and_signs_in_child_once()
    {
        var bootstrap = await BootstrapAdultAsync("123456");
        var childId = Guid.NewGuid();
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.HouseholdMembers.Add(new HouseholdMember(childId, "Fredster", false));
            database.MemberCredentials.Add(new MemberCredential(childId));
            await database.SaveChangesAsync();
        }

        using var handoff = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            $"/api/users/{childId}/pin-setup",
            bootstrap.Body.AccessToken,
            new { surname = "Avenant" });
        Assert.Equal(HttpStatusCode.OK, handoff.StatusCode);
        var grant = (await handoff.Content.ReadFromJsonAsync<PinSetupGrantResponse>())!;

        using var revokedAdult = await SendAuthorizedAsync(
            HttpMethod.Get,
            "/api/today",
            bootstrap.Body.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedAdult.StatusCode);

        using var setup = await Client.PostAsJsonAsync(
            "/api/auth/setup-pin",
            new { setupToken = grant.SetupToken, pin = "0123" });
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var childAuth = (await setup.Content.ReadFromJsonAsync<AuthBody>())!;
        Assert.Equal("child", childAuth.Member.Role);

        using var replay = await Client.PostAsJsonAsync(
            "/api/auth/setup-pin",
            new { setupToken = grant.SetupToken, pin = "0123" });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal("invalid_or_expired_setup", await ProblemCodeAsync(replay));

        using var childAdd = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            "/api/today/jobs",
            childAuth.AccessToken,
            new { childId, name = "No", description = "Forbidden", points = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, childAdd.StatusCode);
    }

    [Fact]
    public async Task Adult_creates_lists_and_onboards_a_child_across_restart()
    {
        var bootstrap = await BootstrapAdultAsync("123456");
        using var createdResponse = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            "/api/users",
            bootstrap.Body.AccessToken,
            new { firstName = "  Fred  ", surname = " Avenant ", nickname = " Fredster ", role = "child" });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<FamilyMemberBody>())!;
        Assert.Equal("Fred", created.FirstName);
        Assert.Equal("Avenant", created.Surname);
        Assert.Equal("Fredster", created.Nickname);
        Assert.Equal("child", created.Role);
        Assert.False(created.IsCredentialReady);
        Assert.Equal($"/api/users/{created.Id}", createdResponse.Headers.Location?.OriginalString);

        await RestartApplicationAsync();
        using var listedResponse = await SendAuthorizedAsync(
            HttpMethod.Get,
            "/api/users",
            bootstrap.Body.AccessToken);
        Assert.Equal(HttpStatusCode.OK, listedResponse.StatusCode);
        var listed = (await listedResponse.Content.ReadFromJsonAsync<IReadOnlyList<FamilyMemberBody>>())!;
        Assert.Contains(listed, member => member.Id == created.Id && !member.IsCredentialReady);
        Assert.Contains(listed, member => member.Id == bootstrap.Body.Member.Id && member.IsCredentialReady);

        using var handoff = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            $"/api/users/{created.Id}/pin-setup",
            bootstrap.Body.AccessToken,
            new { surname = (string?)null });
        var grant = (await handoff.Content.ReadFromJsonAsync<PinSetupGrantResponse>())!;
        using var setup = await Client.PostAsJsonAsync(
            "/api/auth/setup-pin",
            new { setupToken = grant.SetupToken, pin = "0123" });
        var child = (await setup.Content.ReadFromJsonAsync<AuthBody>())!;

        using var childList = await SendAuthorizedAsync(HttpMethod.Get, "/api/users", child.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, childList.StatusCode);
        using var childInactiveList = await SendAuthorizedAsync(
            HttpMethod.Get,
            "/api/users?includeInactive=true",
            child.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, childInactiveList.StatusCode);
        using var childCreate = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            "/api/users",
            child.AccessToken,
            new { firstName = "No", surname = "Access", nickname = (string?)null, role = "child" });
        Assert.Equal(HttpStatusCode.Forbidden, childCreate.StatusCode);
        using var childUpdate = await SendAuthorizedJsonAsync(
            HttpMethod.Patch,
            $"/api/users/{created.Id}",
            child.AccessToken,
            new { firstName = "No", surname = "Access", nickname = (string?)null });
        Assert.Equal(HttpStatusCode.Forbidden, childUpdate.StatusCode);
        using var childDeactivate = await SendAuthorizedAsync(
            HttpMethod.Delete,
            $"/api/users/{created.Id}",
            child.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, childDeactivate.StatusCode);
        using var childRestore = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            $"/api/users/{created.Id}/restore",
            child.AccessToken,
            new { });
        Assert.Equal(HttpStatusCode.Forbidden, childRestore.StatusCode);
    }

    [Fact]
    public async Task Adult_resets_another_members_PIN_with_a_single_use_private_handoff()
    {
        var bootstrap = await BootstrapAdultAsync("123456");
        using var createdResponse = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            "/api/users",
            bootstrap.Body.AccessToken,
            new { firstName = "Fred", surname = "Avenant", nickname = "Fredster", role = "child" });
        var created = (await createdResponse.Content.ReadFromJsonAsync<FamilyMemberBody>())!;
        using var initialHandoff = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            $"/api/users/{created.Id}/pin-setup",
            bootstrap.Body.AccessToken,
            new { surname = (string?)null });
        var initialGrant = (await initialHandoff.Content.ReadFromJsonAsync<PinSetupGrantResponse>())!;
        using var initialSetup = await Client.PostAsJsonAsync(
            "/api/auth/setup-pin",
            new { setupToken = initialGrant.SetupToken, pin = "0123" });
        var child = (await initialSetup.Content.ReadFromJsonAsync<AuthBody>())!;
        var childRefresh = RefreshCookie(initialSetup);
        using var childReset = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/api/users/{bootstrap.Body.Member.Id}/pin-reset",
            child.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, childReset.StatusCode);

        using var adultSignIn = await Client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { memberId = bootstrap.Body.Member.Id, pin = "123456" });
        var adult = (await adultSignIn.Content.ReadFromJsonAsync<AuthBody>())!;
        using var job = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            "/api/today/jobs",
            adult.AccessToken,
            new
            {
                childIds = new[] { created.Id },
                name = "Keep this history",
                description = "PIN reset must not delete it.",
                points = 2,
                scheduledDate = Factory.Clock.Today,
                agendaPeriod = "unscheduled",
                scheduledTime = (string?)null,
            });
        Assert.Equal(HttpStatusCode.Created, job.StatusCode);

        var resetAt = Factory.Clock.UtcNow;
        using var reset = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/api/users/{created.Id}/pin-reset",
            adult.AccessToken);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var resetGrant = (await reset.Content.ReadFromJsonAsync<PinSetupGrantResponse>())!;
        Assert.Equal(created.DisplayName, resetGrant.TargetDisplayName);
        Assert.Equal("child", resetGrant.TargetRole);
        Assert.True(resetGrant.ExpiresAtUtc - resetAt <= TimeSpan.FromMinutes(5));

        using var revokedChildAccess = await SendAuthorizedAsync(
            HttpMethod.Get,
            "/api/today",
            child.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedChildAccess.StatusCode);
        using var revokedChildRefresh = await RefreshAsync(childRefresh);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedChildRefresh.StatusCode);
        using var revokedAdultAccess = await SendAuthorizedAsync(
            HttpMethod.Get,
            "/api/today",
            adult.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedAdultAccess.StatusCode);
        using var oldPin = await Client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { memberId = created.Id, pin = "0123" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPin.StatusCode);
        Assert.Equal("invalid_credentials", await ProblemCodeAsync(oldPin));

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var credential = await database.MemberCredentials.SingleAsync(
                candidate => candidate.MemberId == created.Id);
            Assert.Equal(CredentialState.NotSet, credential.State);
            Assert.Null(credential.PinHash);
            Assert.Null(credential.PinSetAtUtc);
            Assert.Equal(resetAt, credential.PinResetAtUtc!.Value, TimeSpan.FromMicroseconds(1));
            Assert.Equal(bootstrap.Body.Member.Id, credential.PinResetByMemberId);
            Assert.Equal(1, await database.Jobs.CountAsync(candidate => candidate.ChildId == created.Id));
            var storedToken = await database.PinSetupTokens.SingleAsync(
                token => token.TargetMemberId == created.Id && token.ConsumedAtUtc == null);
            Assert.NotEqual(resetGrant.SetupToken, storedToken.TokenHash);
            Assert.DoesNotContain(resetGrant.SetupToken, storedToken.TokenHash, StringComparison.Ordinal);
        }

        using var replacement = await Client.PostAsJsonAsync(
            "/api/auth/setup-pin",
            new { setupToken = resetGrant.SetupToken, pin = "9876" });
        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
        using var replay = await Client.PostAsJsonAsync(
            "/api/auth/setup-pin",
            new { setupToken = resetGrant.SetupToken, pin = "9876" });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal("invalid_or_expired_setup", await ProblemCodeAsync(replay));

        await RestartApplicationAsync();
        using var persistedSignIn = await Client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { memberId = created.Id, pin = "9876" });
        Assert.Equal(HttpStatusCode.OK, persistedSignIn.StatusCode);
        await using var restartedScope = Factory.Services.CreateAsyncScope();
        Assert.Equal(1, await restartedScope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Jobs.CountAsync(candidate => candidate.ChildId == created.Id));
    }

    [Fact]
    public async Task PIN_reset_rejects_self_unknown_unconfigured_and_inactive_targets_without_mutation()
    {
        var bootstrap = await BootstrapAdultAsync("123456");
        using var self = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/api/users/{bootstrap.Body.Member.Id}/pin-reset",
            bootstrap.Body.AccessToken);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal("cannot_reset_self", await ProblemCodeAsync(self));

        using var unknown = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/api/users/{Guid.NewGuid()}/pin-reset",
            bootstrap.Body.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("member_not_found", await ProblemCodeAsync(unknown));

        using var createdResponse = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            "/api/users",
            bootstrap.Body.AccessToken,
            new { firstName = "Harrie", surname = "Avenant", nickname = (string?)null, role = "child" });
        var created = (await createdResponse.Content.ReadFromJsonAsync<FamilyMemberBody>())!;
        using var notSet = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/api/users/{created.Id}/pin-reset",
            bootstrap.Body.AccessToken);
        Assert.Equal(HttpStatusCode.Conflict, notSet.StatusCode);
        Assert.Equal("pin_not_set", await ProblemCodeAsync(notSet));

        using var deactivated = await SendAuthorizedAsync(
            HttpMethod.Delete,
            $"/api/users/{created.Id}",
            bootstrap.Body.AccessToken);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        using var inactive = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/api/users/{created.Id}/pin-reset",
            bootstrap.Body.AccessToken);
        Assert.Equal(HttpStatusCode.Conflict, inactive.StatusCode);
        Assert.Equal("member_not_eligible", await ProblemCodeAsync(inactive));

        await using var scope = Factory.Services.CreateAsyncScope();
        var credential = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .MemberCredentials.SingleAsync(candidate => candidate.MemberId == created.Id);
        Assert.Null(credential.PinResetAtUtc);
        Assert.Null(credential.PinResetByMemberId);
    }

    [Fact]
    public async Task Concurrent_PIN_resets_have_at_most_one_winner()
    {
        var bootstrap = await BootstrapAdultAsync("123456");
        using var createdResponse = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            "/api/users",
            bootstrap.Body.AccessToken,
            new { firstName = "Fred", surname = "Avenant", nickname = (string?)null, role = "child" });
        var child = (await createdResponse.Content.ReadFromJsonAsync<FamilyMemberBody>())!;
        using var handoff = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            $"/api/users/{child.Id}/pin-setup",
            bootstrap.Body.AccessToken,
            new { surname = (string?)null });
        var setupGrant = (await handoff.Content.ReadFromJsonAsync<PinSetupGrantResponse>())!;
        using var setup = await Client.PostAsJsonAsync(
            "/api/auth/setup-pin",
            new { setupToken = setupGrant.SetupToken, pin = "0123" });
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);

        using var firstSignIn = await Client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { memberId = bootstrap.Body.Member.Id, pin = "123456" });
        using var secondSignIn = await Client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { memberId = bootstrap.Body.Member.Id, pin = "123456" });
        var firstAdult = (await firstSignIn.Content.ReadFromJsonAsync<AuthBody>())!;
        var secondAdult = (await secondSignIn.Content.ReadFromJsonAsync<AuthBody>())!;

        var attempts = await Task.WhenAll(
            SendAuthorizedAsync(
                HttpMethod.Post,
                $"/api/users/{child.Id}/pin-reset",
                firstAdult.AccessToken),
            SendAuthorizedAsync(
                HttpMethod.Post,
                $"/api/users/{child.Id}/pin-reset",
                secondAdult.AccessToken));
        using var firstAttempt = attempts[0];
        using var secondAttempt = attempts[1];

        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.OK);
        var rejected = Assert.Single(attempts, response => response.StatusCode != HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal("pin_not_set", await ProblemCodeAsync(rejected));
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await database.PinSetupTokens.CountAsync(
            token => token.TargetMemberId == child.Id
                && token.ConsumedAtUtc == null
                && token.RevokedAtUtc == null));
    }

    [Fact]
    public async Task Adult_edits_deactivates_and_restores_a_child_without_losing_history()
    {
        var bootstrap = await BootstrapAdultAsync("123456");
        using var createdResponse = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            "/api/users",
            bootstrap.Body.AccessToken,
            new { firstName = "Fred", surname = "Avenant", nickname = "Fredster", role = "child" });
        var created = (await createdResponse.Content.ReadFromJsonAsync<FamilyMemberBody>())!;

        using var handoff = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            $"/api/users/{created.Id}/pin-setup",
            bootstrap.Body.AccessToken,
            new { surname = (string?)null });
        var grant = (await handoff.Content.ReadFromJsonAsync<PinSetupGrantResponse>())!;
        using var setup = await Client.PostAsJsonAsync(
            "/api/auth/setup-pin",
            new { setupToken = grant.SetupToken, pin = "0123" });
        var childAuth = (await setup.Content.ReadFromJsonAsync<AuthBody>())!;
        var childRefresh = RefreshCookie(setup);

        using var childDelete = await SendAuthorizedAsync(
            HttpMethod.Delete,
            $"/api/users/{created.Id}",
            childAuth.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, childDelete.StatusCode);

        using var adultSignIn = await Client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { memberId = bootstrap.Body.Member.Id, pin = "123456" });
        var adultAuth = (await adultSignIn.Content.ReadFromJsonAsync<AuthBody>())!;
        using var job = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            "/api/today/jobs",
            adultAuth.AccessToken,
            new
            {
                childIds = new[] { created.Id },
                name = "Keep this job",
                description = "History survives deactivation.",
                points = 2,
                scheduledDate = Factory.Clock.Today,
                agendaPeriod = "unscheduled",
                scheduledTime = (string?)null,
            });
        Assert.Equal(HttpStatusCode.Created, job.StatusCode);

        var editedAt = Factory.Clock.UtcNow;
        using var editedResponse = await SendAuthorizedJsonAsync(
            HttpMethod.Patch,
            $"/api/users/{created.Id}",
            adultAuth.AccessToken,
            new { firstName = " Frederick ", surname = " Smith ", nickname = " Addie " });
        Assert.Equal(HttpStatusCode.OK, editedResponse.StatusCode);
        var edited = (await editedResponse.Content.ReadFromJsonAsync<FamilyMemberBody>())!;
        Assert.Equal("Frederick", edited.FirstName);
        Assert.Equal("Smith", edited.Surname);
        Assert.Equal("Addie", edited.Nickname);
        Assert.True(edited.IsActive);

        using (var startBeforeDeactivate = await Client.GetAsync("/api/auth/start"))
        {
            var start = (await startBeforeDeactivate.Content.ReadFromJsonAsync<AuthStart>())!;
            Assert.Contains(start.Members!, member => member.DisplayName == "Addie Avenant");
            Assert.Contains(start.Members!, member => member.DisplayName == "Addie Smith");
        }

        var deactivatedAt = Factory.Clock.UtcNow;
        using var deactivatedResponse = await SendAuthorizedAsync(
            HttpMethod.Delete,
            $"/api/users/{created.Id}",
            adultAuth.AccessToken);
        Assert.Equal(HttpStatusCode.OK, deactivatedResponse.StatusCode);
        var deactivated = (await deactivatedResponse.Content.ReadFromJsonAsync<FamilyMemberBody>())!;
        Assert.False(deactivated.IsActive);
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        using var repeatedDeactivation = await SendAuthorizedAsync(
            HttpMethod.Delete,
            $"/api/users/{created.Id}",
            adultAuth.AccessToken);
        Assert.Equal(HttpStatusCode.OK, repeatedDeactivation.StatusCode);
        Assert.False((await repeatedDeactivation.Content.ReadFromJsonAsync<FamilyMemberBody>())!.IsActive);

        using var activeResponse = await SendAuthorizedAsync(
            HttpMethod.Get,
            "/api/users",
            adultAuth.AccessToken);
        var active = (await activeResponse.Content.ReadFromJsonAsync<IReadOnlyList<FamilyMemberBody>>())!;
        Assert.DoesNotContain(active, member => member.Id == created.Id);
        using var allResponse = await SendAuthorizedAsync(
            HttpMethod.Get,
            "/api/users?includeInactive=true",
            adultAuth.AccessToken);
        var all = (await allResponse.Content.ReadFromJsonAsync<IReadOnlyList<FamilyMemberBody>>())!;
        Assert.Contains(all, member => member.Id == created.Id && !member.IsActive);

        using var revokedAccess = await SendAuthorizedAsync(
            HttpMethod.Get,
            "/api/today",
            childAuth.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedAccess.StatusCode);
        using var revokedRefresh = await RefreshAsync(childRefresh);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedRefresh.StatusCode);

        var startAfterDeactivate = await Client.GetFromJsonAsync<AuthStart>("/api/auth/start");
        Assert.DoesNotContain(startAfterDeactivate!.Members!, member => member.Id == created.Id);
        Assert.Contains(startAfterDeactivate.Members!, member => member.DisplayName == "Addie");

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await database.HouseholdMembers.SingleAsync(member => member.Id == created.Id);
            Assert.Equal(
                editedAt,
                stored.ProfileUpdatedAtUtc!.Value,
                TimeSpan.FromMicroseconds(1));
            Assert.Equal(bootstrap.Body.Member.Id, stored.ProfileUpdatedByMemberId);
            Assert.Equal(
                deactivatedAt,
                stored.DeactivatedAtUtc!.Value,
                TimeSpan.FromMicroseconds(1));
            Assert.Equal(bootstrap.Body.Member.Id, stored.DeactivatedByMemberId);
            Assert.Equal(1, await database.Jobs.CountAsync(candidate => candidate.ChildId == created.Id));
        }

        await RestartApplicationAsync();
        var restoredAt = Factory.Clock.UtcNow;
        using var restoredResponse = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            $"/api/users/{created.Id}/restore",
            adultAuth.AccessToken,
            new { });
        Assert.Equal(HttpStatusCode.OK, restoredResponse.StatusCode);
        var restored = (await restoredResponse.Content.ReadFromJsonAsync<FamilyMemberBody>())!;
        Assert.True(restored.IsActive);
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        using var repeatedRestore = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            $"/api/users/{created.Id}/restore",
            adultAuth.AccessToken,
            new { });
        Assert.Equal(HttpStatusCode.OK, repeatedRestore.StatusCode);
        Assert.True((await repeatedRestore.Content.ReadFromJsonAsync<FamilyMemberBody>())!.IsActive);

        using var childSignIn = await Client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { memberId = created.Id, pin = "0123" });
        Assert.Equal(HttpStatusCode.OK, childSignIn.StatusCode);
        await using var restoredScope = Factory.Services.CreateAsyncScope();
        var restoredDatabase = restoredScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var restoredMember = await restoredDatabase.HouseholdMembers.SingleAsync(
            member => member.Id == created.Id);
        Assert.Equal(
            restoredAt,
            restoredMember.RestoredAtUtc!.Value,
            TimeSpan.FromMicroseconds(1));
        Assert.Equal(bootstrap.Body.Member.Id, restoredMember.RestoredByMemberId);
        Assert.Equal(1, await restoredDatabase.Jobs.CountAsync(candidate => candidate.ChildId == created.Id));
    }

    [Fact]
    public async Task Deactivation_revokes_an_outstanding_PIN_handoff()
    {
        var bootstrap = await BootstrapAdultAsync("123456");
        using var createdResponse = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            "/api/users",
            bootstrap.Body.AccessToken,
            new { firstName = "Harrie", surname = "Avenant", nickname = (string?)null, role = "child" });
        var created = (await createdResponse.Content.ReadFromJsonAsync<FamilyMemberBody>())!;
        using var handoff = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            $"/api/users/{created.Id}/pin-setup",
            bootstrap.Body.AccessToken,
            new { surname = (string?)null });
        var grant = (await handoff.Content.ReadFromJsonAsync<PinSetupGrantResponse>())!;

        using var adultSignIn = await Client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { memberId = bootstrap.Body.Member.Id, pin = "123456" });
        var adult = (await adultSignIn.Content.ReadFromJsonAsync<AuthBody>())!;
        using var deactivated = await SendAuthorizedAsync(
            HttpMethod.Delete,
            $"/api/users/{created.Id}",
            adult.AccessToken);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);

        using var setup = await Client.PostAsJsonAsync(
            "/api/auth/setup-pin",
            new { setupToken = grant.SetupToken, pin = "0123" });
        Assert.Equal(HttpStatusCode.BadRequest, setup.StatusCode);
        Assert.Equal("invalid_or_expired_setup", await ProblemCodeAsync(setup));

        using var restored = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            $"/api/users/{created.Id}/restore",
            adult.AccessToken,
            new { });
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.False((await restored.Content.ReadFromJsonAsync<FamilyMemberBody>())!.IsCredentialReady);
        using var newHandoff = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            $"/api/users/{created.Id}/pin-setup",
            adult.AccessToken,
            new { surname = (string?)null });
        Assert.Equal(HttpStatusCode.OK, newHandoff.StatusCode);
    }

    [Fact]
    public async Task Adult_cannot_deactivate_their_current_profile()
    {
        var bootstrap = await BootstrapAdultAsync("123456");

        using var response = await SendAuthorizedAsync(
            HttpMethod.Delete,
            $"/api/users/{bootstrap.Body.Member.Id}",
            bootstrap.Body.AccessToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("cannot_deactivate_self", await ProblemCodeAsync(response));
        using var stillAuthorized = await SendAuthorizedAsync(
            HttpMethod.Get,
            "/api/today",
            bootstrap.Body.AccessToken);
        Assert.Equal(HttpStatusCode.OK, stillAuthorized.StatusCode);
    }

    [Fact]
    public async Task Invalid_member_update_is_atomic_and_returns_a_stable_problem()
    {
        var bootstrap = await BootstrapAdultAsync("123456");
        using var response = await SendAuthorizedJsonAsync(
            HttpMethod.Patch,
            $"/api/users/{bootstrap.Body.Member.Id}",
            bootstrap.Body.AccessToken,
            new { firstName = "", surname = "Avenant", nickname = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_member", await ProblemCodeAsync(response));
        await using var scope = Factory.Services.CreateAsyncScope();
        var member = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .HouseholdMembers.SingleAsync(candidate => candidate.Id == bootstrap.Body.Member.Id);
        Assert.Equal("Addie", member.FirstName);
        Assert.Null(member.ProfileUpdatedAtUtc);
    }

    [Fact]
    public async Task Duplicate_display_names_are_allowed_and_disambiguated_in_the_list()
    {
        var bootstrap = await BootstrapAdultAsync("123456");
        foreach (var surname in new[] { "Avenant", "Smith" })
        {
            using var response = await SendAuthorizedJsonAsync(
                HttpMethod.Post,
                "/api/users",
                bootstrap.Body.AccessToken,
                new { firstName = "Sam", surname, nickname = (string?)null, role = "child" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        using var listedResponse = await SendAuthorizedAsync(
            HttpMethod.Get,
            "/api/users",
            bootstrap.Body.AccessToken);
        var members = (await listedResponse.Content.ReadFromJsonAsync<IReadOnlyList<FamilyMemberBody>>())!;
        Assert.Contains(members, member => member.DisplayName == "Sam Avenant");
        Assert.Contains(members, member => member.DisplayName == "Sam Smith");
    }

    [Fact]
    public async Task Inactive_member_cannot_receive_a_PIN_handoff()
    {
        var bootstrap = await BootstrapAdultAsync("123456");
        var inactiveId = Guid.NewGuid();
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.HouseholdMembers.Add(new HouseholdMember(
                inactiveId,
                "Archived",
                "Member",
                HouseholdRole.Child,
                isActive: false));
            database.MemberCredentials.Add(new MemberCredential(inactiveId));
            await database.SaveChangesAsync();
        }

        using var response = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            $"/api/users/{inactiveId}/pin-setup",
            bootstrap.Body.AccessToken,
            new { surname = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("member_not_eligible", await ProblemCodeAsync(response));
    }

    [Theory]
    [InlineData("", "Avenant", "child")]
    [InlineData("Fred", "", "child")]
    [InlineData("Fred", "Avenant", "visitor")]
    public async Task Invalid_member_creation_is_atomic_and_returns_a_stable_problem(
        string firstName,
        string surname,
        string role)
    {
        var bootstrap = await BootstrapAdultAsync("123456");
        using var response = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            "/api/users",
            bootstrap.Body.AccessToken,
            new { firstName, surname, nickname = (string?)null, role });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_member", await ProblemCodeAsync(response));
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await database.HouseholdMembers.CountAsync());
        Assert.Equal(1, await database.MemberCredentials.CountAsync());
    }

    [Fact]
    public async Task Pilot_migration_preserves_rows_and_exposes_only_claimable_adults()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("family_jobs_board_pilot_upgrade")
            .WithUsername("family_jobs_board")
            .WithPassword("family_jobs_board")
            .Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using (var database = new AppDbContext(options))
        {
            var migrator = database.GetService<IMigrator>();
            await migrator.MigrateAsync("20260902124933_AddMonthlyRecurringJobs");
            var jobId = Guid.NewGuid();
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO household_members (id, first_name, nickname, is_adult) VALUES
                ({DemoDataIds.Addie}, 'Addie', NULL, TRUE),
                ({DemoDataIds.Hellie}, 'Hellie', NULL, TRUE),
                ({DemoDataIds.Fredster}, 'Fredster', NULL, FALSE);
                INSERT INTO jobs (id, child_id, name, description, points, scheduled_date, status, agenda_period)
                VALUES ({jobId}, {DemoDataIds.Fredster}, 'Existing job', 'Keep me.', 3, DATE '2026-09-06', 'Open', 'Unscheduled');
                """);
            await migrator.MigrateAsync();
            Assert.Equal(3, await database.HouseholdMembers.CountAsync());
            Assert.Equal(3, await database.MemberCredentials.CountAsync(
                credential => credential.State == CredentialState.NotSet));
            Assert.Equal(jobId, (await database.Jobs.SingleAsync()).Id);
        }

        await using var factory = new IdentityApiFactory(postgres.GetConnectionString());
        using var client = CreateClient(factory);
        var start = await client.GetFromJsonAsync<AuthStart>("/api/auth/start");
        Assert.Equal("claimExistingAdult", start?.State);
        Assert.Equal(["Addie", "Hellie"], start!.Adults!.Select(adult => adult.DisplayName).Order());
        Assert.DoesNotContain(start.Adults!, adult => adult.Id == DemoDataIds.Fredster);

        using var claim = await client.PostAsJsonAsync(
            "/api/auth/bootstrap",
            new
            {
                mode = "claimExistingAdult",
                memberId = DemoDataIds.Addie,
                surname = "Avenant",
                pin = "012345",
            });
        Assert.Equal(HttpStatusCode.Created, claim.StatusCode);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verified = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(3, await verified.HouseholdMembers.CountAsync());
        Assert.Equal(1, await verified.Jobs.CountAsync());
        Assert.Equal(CredentialState.Ready, (await verified.MemberCredentials.SingleAsync(
            credential => credential.MemberId == DemoDataIds.Addie)).State);
    }

    [Fact]
    public void Production_authentication_configuration_rejects_missing_secrets()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:AllowedOrigin"] = "http://dashboard.home.arpa",
            })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() =>
            ServiceCollectionExtensions.GetAuthenticationOptions(
                configuration,
                new ProductionEnvironment()));

        Assert.Contains("Authentication:PinPepper is required", error.Message);
        Assert.DoesNotContain("PinHash", error.Message);
        Assert.DoesNotContain("JwtSigningKey=", error.Message);
    }

    private async Task<(HttpResponseMessage Response, AuthBody Body)> BootstrapAdultAsync(string pin)
    {
        var response = await Client.PostAsJsonAsync(
            "/api/auth/bootstrap",
            new { mode = "createFirstAdult", firstName = "Addie", surname = "Avenant", pin });
        var body = (await response.Content.ReadFromJsonAsync<AuthBody>())!;
        return (response, body);
    }

    private async Task LogoutAsync(AuthBody auth)
    {
        using var response = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/api/auth/logout",
            auth.AccessToken,
            includeOrigin: true);
        response.EnsureSuccessStatusCode();
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.TryAddWithoutValidation("Origin", Origin);
        request.Headers.TryAddWithoutValidation("Cookie", $"family_jobs_board_refresh={refreshToken}");
        return Client.SendAsync(request);
    }

    private Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpMethod method,
        string path,
        string accessToken,
        bool includeOrigin = false)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (includeOrigin)
        {
            request.Headers.TryAddWithoutValidation("Origin", Origin);
        }

        return Client.SendAsync(request);
    }

    private Task<HttpResponseMessage> SendAuthorizedJsonAsync<T>(
        HttpMethod method,
        string path,
        string accessToken,
        T body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return Client.SendAsync(request);
    }

    private static string RefreshCookie(HttpResponseMessage response)
    {
        var header = Assert.Single(response.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("family_jobs_board_refresh=", StringComparison.Ordinal));
        return header.Split(';', 2)[0].Split('=', 2)[1];
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("code").GetString();
    }

    private async Task RestartApplicationAsync()
    {
        var clock = Factory.Clock;
        _client?.Dispose();
        await Factory.DisposeAsync();
        _factory = new IdentityApiFactory(_postgres.GetConnectionString(), clock);
        _client = CreateClient(_factory);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(Origin),
            HandleCookies = false,
        });

    private IdentityApiFactory Factory =>
        _factory ?? throw new InvalidOperationException("Test API was not initialised.");

    private HttpClient Client =>
        _client ?? throw new InvalidOperationException("Test client was not initialised.");

    private sealed record AuthStart(string State, IReadOnlyList<StartMember>? Adults, IReadOnlyList<StartMember>? Members);
    private sealed record StartMember(Guid Id, string DisplayName, string Role, bool RequiresSurname);
    private sealed record AuthBody(string AccessToken, DateTimeOffset AccessTokenExpiresAtUtc, AuthMember Member);
    private sealed record AuthMember(Guid Id, string DisplayName, string Role);
    private sealed record PinSetupGrantResponse(
        string SetupToken,
        DateTimeOffset ExpiresAtUtc,
        string TargetDisplayName,
        string TargetRole);
    private sealed record FamilyMemberBody(
        Guid Id,
        string FirstName,
        string? Surname,
        string? Nickname,
        string DisplayName,
        string Role,
        bool IsCredentialReady,
        bool IsActive);

    private sealed class IdentityApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public IdentityApiFactory(string connectionString, MutableClock? clock = null)
        {
            _connectionString = connectionString;
            Clock = clock ?? new MutableClock(DateTimeOffset.UtcNow);
        }

        public MutableClock Clock { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Database"] = _connectionString,
                    ["Household:TimeZone"] = "Africa/Johannesburg",
                    ["Authentication:PinPepper"] = Convert.ToBase64String(new byte[32].Select((_, index) => (byte)(index + 1)).ToArray()),
                    ["Authentication:JwtSigningKey"] = Convert.ToBase64String(new byte[32].Select((_, index) => (byte)(index + 33)).ToArray()),
                    ["Authentication:AllowedOrigin"] = Origin,
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.RemoveAll<IHouseholdClock>();
                services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_connectionString));
                services.AddSingleton<IHouseholdClock>(Clock);
            });
        }
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IHouseholdClock
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.DateTime);
        public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
    }

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "FamilyJobsBoard.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
