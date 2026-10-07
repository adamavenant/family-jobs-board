using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace FamilyJobsBoard.Api.Features.Telemetry;

internal static class TelemetryServiceCollectionExtensions
{
    private const string AspNetCoreDiagnosticsMeter = "Microsoft.AspNetCore.Diagnostics";
    private const string AspNetCoreHostingMeter = "Microsoft.AspNetCore.Hosting";
    private const string SystemRuntimeMeter = "System.Runtime";

    private static readonly string[] HttpRequestTags =
    [
        "http.request.method",
        "http.route",
        "http.response.status_code",
    ];

    internal static readonly IReadOnlyDictionary<string, string[]> RuntimeMetricTags =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["dotnet.assembly.count"] = [],
            ["dotnet.gc.collections"] = ["gc.heap.generation"],
            ["dotnet.gc.heap.total_allocated"] = [],
            ["dotnet.gc.last_collection.heap.fragmentation.size"] = ["gc.heap.generation"],
            ["dotnet.gc.last_collection.heap.size"] = ["gc.heap.generation"],
            ["dotnet.gc.last_collection.memory.committed_size"] = [],
            ["dotnet.gc.pause.time"] = [],
            ["dotnet.jit.compilation.time"] = [],
            ["dotnet.jit.compiled_il.size"] = [],
            ["dotnet.jit.compiled_methods"] = [],
            ["dotnet.monitor.lock_contentions"] = [],
            ["dotnet.process.cpu.count"] = [],
            ["dotnet.process.cpu.time"] = ["cpu.mode"],
            ["dotnet.process.memory.working_set"] = [],
            ["dotnet.thread_pool.work_item.count"] = [],
            ["dotnet.timer.count"] = [],
        };

    public static IServiceCollection AddApplicationTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = ApplicationTelemetryOptions.FromConfiguration(configuration, environment);
        services.AddSingleton(options);
        services.AddSingleton<ApplicationTelemetry>();
        services.AddSingleton<TelemetryEventWriter>();
        services.AddSingleton<ILoggerProvider, ApplicationEventMetricsLoggerProvider>();
        services.AddExceptionHandler<SanitizedExceptionHandler>();
        services.Configure<ExceptionHandlerOptions>(exceptionHandler =>
            exceptionHandler.SuppressDiagnosticsCallback = _ => false);

        if (options.LogsExport is { } logsExport)
        {
            services.AddLogging(logging => ConfigureSafeEventLogging(
                logging,
                options,
                loggerOptions => loggerOptions.AddOtlpExporter(
                    (exporter, processor) =>
                    {
                        exporter.Endpoint = logsExport.Endpoint;
                        exporter.Protocol = logsExport.Protocol;
                        exporter.TimeoutMilliseconds = 5_000;
                        processor.ExportProcessorType = ExportProcessorType.Batch;
                        processor.BatchExportProcessorOptions = new()
                        {
                            MaxQueueSize = 2_048,
                            MaxExportBatchSize = 512,
                            ScheduledDelayMilliseconds = 5_000,
                            ExporterTimeoutMilliseconds = 5_000,
                        };
                    })));
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => AddResource(resource, options))
            .WithMetrics(metrics => ConfigureMetrics(metrics, options));

        return services;
    }

    internal static ILoggingBuilder ConfigureSafeEventLogging(
        ILoggingBuilder logging,
        ApplicationTelemetryOptions options,
        Action<OpenTelemetryLoggerOptions> configureExporter)
    {
        logging.AddFilter<OpenTelemetryLoggerProvider>(
            (category, _) => string.Equals(
                category,
                ApplicationEventMetricsLoggerProvider.TelemetryCategory,
                StringComparison.Ordinal));
        logging.AddOpenTelemetry(loggerOptions =>
        {
            loggerOptions.IncludeFormattedMessage = false;
            loggerOptions.IncludeScopes = false;
            loggerOptions.ParseStateValues = true;
            loggerOptions.SetResourceBuilder(CreateResourceBuilder(options));
            configureExporter(loggerOptions);
        });
        return logging;
    }

    internal static MeterProviderBuilder ConfigureMetrics(
        MeterProviderBuilder metrics,
        ApplicationTelemetryOptions options)
    {
        metrics
            .AddMeter(ApplicationTelemetry.MeterName)
            .AddMeter(AspNetCoreDiagnosticsMeter)
            .AddMeter(AspNetCoreHostingMeter)
            .AddMeter(SystemRuntimeMeter)
            .SetExemplarFilter(ExemplarFilterType.AlwaysOff)
            .AddView(CreateMetricView);

        if (options.MetricsExport is { } metricsExport)
        {
            metrics.AddOtlpExporter(
                (exporter, reader) =>
                {
                    exporter.Endpoint = metricsExport.Endpoint;
                    exporter.Protocol = metricsExport.Protocol;
                    exporter.TimeoutMilliseconds = 5_000;
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 30_000;
                    reader.PeriodicExportingMetricReaderOptions.ExportTimeoutMilliseconds = 5_000;
                });
        }

        return metrics;
    }

    private static MetricStreamConfiguration CreateMetricView(Instrument instrument)
    {
        if (instrument.Meter.Name == AspNetCoreHostingMeter)
        {
            return instrument.Name == "http.server.request.duration"
                ? new ExplicitBucketHistogramConfiguration
                {
                    Boundaries =
                    [
                        0.005,
                        0.01,
                        0.025,
                        0.05,
                        0.1,
                        0.25,
                        0.5,
                        1,
                        2.5,
                        5,
                        10,
                    ],
                    TagKeys = HttpRequestTags,
                }
                : MetricStreamConfiguration.Drop;
        }

        if (instrument.Meter.Name == AspNetCoreDiagnosticsMeter)
        {
            return instrument.Name == "aspnetcore.diagnostics.exceptions"
                ? new MetricStreamConfiguration
                {
                    TagKeys = ["aspnetcore.diagnostics.exception.result"],
                }
                : MetricStreamConfiguration.Drop;
        }

        if (instrument.Meter.Name == SystemRuntimeMeter)
        {
            return RuntimeMetricTags.TryGetValue(instrument.Name, out var tagKeys)
                ? new MetricStreamConfiguration { TagKeys = tagKeys }
                : MetricStreamConfiguration.Drop;
        }

        if (instrument.Meter.Name == ApplicationTelemetry.MeterName)
        {
            return instrument.Name switch
            {
                "family_jobs_board.application.runtime_error.count" =>
                    new MetricStreamConfiguration { TagKeys = ["event.severity"] },
                "family_jobs_board.application.warning.count" =>
                    new MetricStreamConfiguration { TagKeys = [] },
                "family_jobs_board.authentication.rejection.count" =>
                    new MetricStreamConfiguration { TagKeys = HttpRequestTags },
                "family_jobs_board.process.start.time" =>
                    new MetricStreamConfiguration { TagKeys = [] },
                _ => MetricStreamConfiguration.Drop,
            };
        }

        return MetricStreamConfiguration.Drop;
    }

    internal static ResourceBuilder CreateResourceBuilder(ApplicationTelemetryOptions options) =>
        AddResource(ResourceBuilder.CreateDefault(), options);

    private static ResourceBuilder AddResource(
        ResourceBuilder resource,
        ApplicationTelemetryOptions options) =>
        resource
            .AddService(
                options.ServiceName,
                serviceVersion: options.ServiceVersion,
                autoGenerateServiceInstanceId: false)
            .AddAttributes(
            [
                new KeyValuePair<string, object>(
                    "deployment.environment.name",
                    options.DeploymentEnvironment),
            ]);
}
