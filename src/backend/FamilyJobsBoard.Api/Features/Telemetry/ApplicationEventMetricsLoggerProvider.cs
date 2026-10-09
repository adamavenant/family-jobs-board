namespace FamilyJobsBoard.Api.Features.Telemetry;

internal sealed class ApplicationEventMetricsLoggerProvider(ApplicationTelemetry telemetry)
    : ILoggerProvider
{
    internal const string TelemetryCategory = "FamilyJobsBoard.Telemetry";
    internal const string IdentityAuditCategory = "IdentityAudit";
    internal const string ExceptionHandlerCategory =
        "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware";
    internal const string KestrelCategory = "Microsoft.AspNetCore.Server.Kestrel";
    internal const int KestrelApplicationErrorEventId = 13;

    public ILogger CreateLogger(string categoryName) =>
        categoryName is IdentityAuditCategory or ExceptionHandlerCategory
            ? NoopLogger.Instance
            : new ApplicationEventMetricsLogger(categoryName, telemetry);

    public void Dispose()
    {
    }

    private sealed class ApplicationEventMetricsLogger(
        string categoryName,
        ApplicationTelemetry telemetry) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (categoryName == KestrelCategory
                && eventId.Id == KestrelApplicationErrorEventId)
            {
                return;
            }

            if (logLevel == LogLevel.Warning)
            {
                telemetry.RecordWarning();
            }
            else if (logLevel is LogLevel.Error or LogLevel.Critical)
            {
                telemetry.RecordRuntimeError(logLevel);
            }
        }
    }

    private sealed class NoopLogger : ILogger
    {
        public static readonly NoopLogger Instance = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
