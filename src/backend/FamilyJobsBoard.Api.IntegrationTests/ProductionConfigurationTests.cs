using FamilyJobsBoard.Api.Composition;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FamilyJobsBoard.Api.IntegrationTests;

public sealed class ProductionConfigurationTests
{
    [Fact]
    public void DatabaseConnectionString_IsRequiredInProduction()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production,
        });
        builder.Configuration.Sources.Clear();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ServiceCollectionExtensions.GetDatabaseConnectionString(
                builder.Configuration,
                builder.Environment));

        Assert.Equal(
            "ConnectionStrings:Database is required when the API runs in Production.",
            exception.Message);
    }

    [Fact]
    public void DatabaseConnectionString_UsesLocalFallbackOutsideProduction()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
        });
        builder.Configuration.Sources.Clear();

        var connectionString = ServiceCollectionExtensions.GetDatabaseConnectionString(
            builder.Configuration,
            builder.Environment);

        Assert.Contains("Host=localhost", connectionString, StringComparison.Ordinal);
    }
}
