using System.Diagnostics.Metrics;

namespace FamilyJobsBoard.Api.Features.Telemetry;

internal sealed class ApplicationTelemetry : IDisposable
{
    internal const string MeterName = "FamilyJobsBoard.Api";

    private readonly Meter _meter;
    private readonly Counter<long> _runtimeErrors;
    private readonly Counter<long> _warnings;
    private readonly Counter<long> _authenticationRejections;
    private readonly DateTimeOffset _startedAtUtc;

    public ApplicationTelemetry()
        : this(MeterName, TimeProvider.System)
    {
    }

    internal ApplicationTelemetry(string meterName, TimeProvider timeProvider)
    {
        _meter = new Meter(meterName, "1.0.0");
        _runtimeErrors = _meter.CreateCounter<long>(
            "family_jobs_board.application.runtime_error.count",
            unit: "{event}",
            description: "Sanitized Error and Critical application events.");
        _warnings = _meter.CreateCounter<long>(
            "family_jobs_board.application.warning.count",
            unit: "{event}",
            description: "Sanitized Warning application events.");
        _authenticationRejections = _meter.CreateCounter<long>(
            "family_jobs_board.authentication.rejection.count",
            unit: "{rejection}",
            description: "Expected authentication and authorization rejections.");
        _startedAtUtc = timeProvider.GetUtcNow();
        _meter.CreateObservableGauge(
            "family_jobs_board.process.start.time",
            () => _startedAtUtc.ToUnixTimeSeconds(),
            unit: "s",
            description: "Unix timestamp when this API process started.");
    }

    public void RecordRuntimeError(LogLevel level)
    {
        var severity = level == LogLevel.Critical ? "critical" : "error";
        _runtimeErrors.Add(1, new KeyValuePair<string, object?>("event.severity", severity));
    }

    public void RecordWarning() => _warnings.Add(1);

    public void RecordAuthenticationRejection(
        string method,
        string route,
        int statusCode)
    {
        _authenticationRejections.Add(
            1,
            new KeyValuePair<string, object?>("http.request.method", method),
            new KeyValuePair<string, object?>("http.route", route),
            new KeyValuePair<string, object?>("http.response.status_code", statusCode));
    }

    public void Dispose() => _meter.Dispose();
}
