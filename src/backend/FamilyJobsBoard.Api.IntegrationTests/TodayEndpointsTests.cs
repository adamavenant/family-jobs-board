using System.Net;
using System.Net.Http.Json;
using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
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
