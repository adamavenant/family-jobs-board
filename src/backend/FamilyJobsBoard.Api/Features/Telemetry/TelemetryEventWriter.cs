using System.Diagnostics;

namespace FamilyJobsBoard.Api.Features.Telemetry;

internal sealed class TelemetryEventWriter
{
    internal const string Schema = "family-jobs-board.event.v1";

    private static readonly EventId ApplicationStartedEvent = new(2000, "ApplicationStarted");
    private static readonly EventId RequestCompletedEvent = new(2001, "HttpRequestCompleted");
    private static readonly EventId AuthenticationRejectedEvent = new(2401, "AuthenticationRejected");
    private static readonly EventId TelemetryConfigurationIgnoredEvent = new(2901, "TelemetryConfigurationIgnored");
    private static readonly EventId UnhandledRequestFailureEvent = new(5001, "UnhandledRequestFailure");

    private readonly ILogger _logger;
    private readonly ApplicationTelemetryOptions _options;

    public TelemetryEventWriter(
        ILoggerFactory loggerFactory,
        ApplicationTelemetryOptions options)
    {
        _logger = loggerFactory.CreateLogger(ApplicationEventMetricsLoggerProvider.TelemetryCategory);
        _options = options;
    }

    public void ApplicationStarted()
    {
        Write(
            LogLevel.Information,
            ApplicationStartedEvent,
            "Application started.",
            "application.started");
    }

    public void RequestCompleted(
        string method,
        string route,
        int statusCode,
        double durationMilliseconds)
    {
        Write(
            LogLevel.Information,
            RequestCompletedEvent,
            "HTTP request completed.",
            "http.request.completed",
            new KeyValuePair<string, object?>("http.request.method", method),
            new KeyValuePair<string, object?>("http.route", route),
            new KeyValuePair<string, object?>("http.response.status_code", statusCode),
            new KeyValuePair<string, object?>(
                "http.response.status_class",
                TelemetryDimensions.StatusClass(statusCode)),
            new KeyValuePair<string, object?>(
                "duration_ms",
                Math.Round(durationMilliseconds, 3)));
    }

    public void AuthenticationRejected(string method, string route, int statusCode)
    {
        Write(
            LogLevel.Information,
            AuthenticationRejectedEvent,
            "Authentication request rejected.",
            "authentication.rejected",
            new KeyValuePair<string, object?>("http.request.method", method),
            new KeyValuePair<string, object?>("http.route", route),
            new KeyValuePair<string, object?>("http.response.status_code", statusCode),
            new KeyValuePair<string, object?>(
                "http.response.status_class",
                TelemetryDimensions.StatusClass(statusCode)));
    }

    public void UnhandledRequestFailure(string method, string route)
    {
        Write(
            LogLevel.Error,
            UnhandledRequestFailureEvent,
            "Unhandled request failure.",
            "http.request.unhandled_failure",
            new KeyValuePair<string, object?>("http.request.method", method),
            new KeyValuePair<string, object?>("http.route", route),
            new KeyValuePair<string, object?>(
                "http.response.status_code",
                StatusCodes.Status500InternalServerError),
            new KeyValuePair<string, object?>("http.response.status_class", "5xx"));
    }

    public void TelemetryConfigurationIgnored(string reason)
    {
        Write(
            LogLevel.Warning,
            TelemetryConfigurationIgnoredEvent,
            "Invalid telemetry exporter configuration was ignored.",
            "telemetry.configuration.ignored",
            new KeyValuePair<string, object?>("reason", reason));
    }

    private void Write(
        LogLevel level,
        EventId eventId,
        string message,
        string eventName,
        params KeyValuePair<string, object?>[] eventAttributes)
    {
        var attributes = new List<KeyValuePair<string, object?>>(5 + eventAttributes.Length)
        {
            new("telemetry.schema", Schema),
            new("service.name", _options.ServiceName),
            new("service.version", _options.ServiceVersion),
            new("deployment.environment.name", _options.DeploymentEnvironment),
            new("event.name", eventName),
        };
        attributes.AddRange(eventAttributes);

        var currentActivity = Activity.Current;
        Activity.Current = null;
        try
        {
            _logger.Log(
                level,
                eventId,
                new TelemetryLogState(message, attributes),
                null,
                static (state, _) => state.Message);
        }
        finally
        {
            Activity.Current = currentActivity;
        }
    }

    private sealed record TelemetryLogState(
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> Attributes)
        : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public int Count => Attributes.Count;

        public KeyValuePair<string, object?> this[int index] => Attributes[index];

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            Attributes.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}
