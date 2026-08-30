using System.Net;
using System.Net.Http.Json;
using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
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
        Assert.Equal("Alex", initial.Child.Name);
        Assert.Equal(3, initial.Jobs.Count);
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

    private sealed record TodayResponse(ChildResponse Child, DateOnly Date, IReadOnlyList<JobResponse> Jobs);

    private sealed record ChildResponse(Guid Id, string Name);

    private sealed record JobResponse(
        Guid Id,
        string Name,
        string Description,
        int Points,
        string Status,
        DateTimeOffset? CompletedAtUtc);

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
