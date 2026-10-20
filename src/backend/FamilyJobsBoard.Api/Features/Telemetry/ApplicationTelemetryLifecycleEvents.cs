namespace FamilyJobsBoard.Api.Features.Telemetry;

internal sealed class ApplicationTelemetryLifecycleEvents(
    IHostApplicationLifetime applicationLifetime,
    TelemetryEventWriter events,
    ApplicationTelemetryOptions options) : IHostedService
{
    private CancellationTokenRegistration _applicationStartedRegistration;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _applicationStartedRegistration = applicationLifetime.ApplicationStarted.Register(() =>
        {
            events.ApplicationStarted();
            if (options.ConfigurationIssue is { } issue)
            {
                events.TelemetryConfigurationIgnored(issue);
            }
        });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _applicationStartedRegistration.Dispose();
        return Task.CompletedTask;
    }
}
