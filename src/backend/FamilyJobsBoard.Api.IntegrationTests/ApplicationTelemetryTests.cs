using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using FamilyJobsBoard.Api.Features.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using Xunit;

namespace FamilyJobsBoard.Api.IntegrationTests;

[Collection(TelemetryTestCollection.Name)]
public sealed class ApplicationTelemetryTests
{
    private static readonly HashSet<string> AllowedHttpMetricTags = new(StringComparer.Ordinal)
    {
        "http.request.method",
        "http.route",
        "http.response.status_code",
    };

    private static readonly HashSet<string> AllowedEventAttributes = new(StringComparer.Ordinal)
    {
        "telemetry.schema",
        "service.name",
        "service.version",
        "deployment.environment.name",
        "event.name",
        "http.request.method",
        "http.route",
        "http.response.status_code",
        "http.response.status_class",
        "duration_ms",
        "reason",
    };

    private static readonly HashSet<string> AllowedGcGenerations = new(StringComparer.Ordinal)
    {
        "gen0",
        "gen1",
        "gen2",
        "loh",
        "poh",
    };

    private static readonly HashSet<string> AllowedCpuModes = new(StringComparer.Ordinal)
    {
        "system",
        "user",
    };

    [Fact]
    public async Task Exported_metrics_and_events_are_bounded_classified_and_sanitized()
    {
        var exportedMetrics = new List<Metric>();
        var options = TestOptions();
        using var metricProvider = TelemetryServiceCollectionExtensions
            .ConfigureMetrics(Sdk.CreateMeterProviderBuilder(), options)
            .AddInMemoryExporter(exportedMetrics)
            .Build();
        var logs = new CapturingLoggerProvider();
        var exportedLogs = new List<LogRecord>();

        await using var app = await StartTestApiAsync(
            logs,
            exportedLogs,
            new Dictionary<string, string?>
            {
                ["OTEL_SERVICE_VERSION"] = "test-version",
            });
        using var client = app.GetTestClient();

        for (var index = 0; index < 75; index++)
        {
            using var success = await client.GetAsync($"/items/{Guid.NewGuid():D}");
            Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);

            using var unmatched = await client.GetAsync(
                $"/not-a-route/{Guid.NewGuid():D}?token=private-token-{index}");
            Assert.Equal(HttpStatusCode.NotFound, unmatched.StatusCode);
        }

        using (var authentication = await client.PostAsync("/api/auth/refresh", null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, authentication.StatusCode);
        }

        using (var warning = await client.GetAsync("/controlled-warning"))
        {
            Assert.Equal(HttpStatusCode.NoContent, warning.StatusCode);
        }

        using (var applicationEvents = await client.GetAsync("/application-events"))
        {
            Assert.Equal(HttpStatusCode.NoContent, applicationEvents.StatusCode);
        }

        var secretGuid = Guid.NewGuid();
        using var failureRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/controlled-failure/{secretGuid:D}?reason=private-reason");
        failureRequest.Headers.Authorization = new("Bearer", "private-access-token");
        failureRequest.Headers.Add("Cookie", "family_jobs_board_refresh=private-refresh-token");
        using var failure = await client.SendAsync(failureRequest);
        var failureBody = await failure.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        Assert.DoesNotContain("private", failureBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secretGuid.ToString(), failureBody, StringComparison.OrdinalIgnoreCase);

        Assert.True(metricProvider.ForceFlush(10_000));

        var requestMetric = Assert.Single(
            exportedMetrics,
            metric => metric.Name == "http.server.request.duration");
        var requestPoints = SnapshotPoints(requestMetric, histogram: true);
        Assert.NotEmpty(requestPoints);
        Assert.All(
            requestPoints,
            point => Assert.True(
                point.Tags.Keys.All(AllowedHttpMetricTags.Contains),
                $"Unexpected HTTP metric tag(s): {string.Join(", ", point.Tags.Keys)}"));
        Assert.Equal(
            75,
            requestPoints.Single(point =>
                Tag(point, "http.route") == "/items/{itemId:guid}"
                && Tag(point, "http.response.status_code") == "204").Count);
        Assert.Equal(
            75,
            requestPoints.Single(point =>
                !point.Tags.ContainsKey("http.route")
                && Tag(point, "http.response.status_code") == "404").Count);

        var allExportedTags = string.Join(
            " ",
            requestPoints.SelectMany(point => point.Tags.Select(tag => $"{tag.Key}={tag.Value}")));
        Assert.DoesNotContain(secretGuid.ToString(), allExportedTags, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", allExportedTags, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            2,
            SumCounter(exportedMetrics, "family_jobs_board.application.runtime_error.count"));
        Assert.Equal(
            2,
            SumCounter(exportedMetrics, "family_jobs_board.application.warning.count"));
        Assert.Equal(
            1,
            SumCounter(exportedMetrics, "aspnetcore.diagnostics.exceptions"));
        Assert.Equal(
            1,
            SumCounter(exportedMetrics, "family_jobs_board.authentication.rejection.count"));

        var telemetryLogs = logs.Entries
            .Where(entry => entry.Category == ApplicationEventMetricsLoggerProvider.TelemetryCategory)
            .ToArray();
        Assert.NotEmpty(telemetryLogs);
        Assert.Single(telemetryLogs, entry => entry.Level == LogLevel.Error);
        Assert.Single(telemetryLogs, entry => entry.Level == LogLevel.Warning);
        Assert.All(telemetryLogs, entry =>
        {
            Assert.Null(entry.Exception);
            Assert.NotEqual(0, entry.EventId.Id);
            Assert.False(string.IsNullOrWhiteSpace(entry.EventId.Name));
            Assert.Equal(TelemetryEventWriter.Schema, entry.Attributes["telemetry.schema"]);
            Assert.True(entry.Attributes.Keys.All(AllowedEventAttributes.Contains));
        });

        var serializedLogs = string.Join(
            " ",
            telemetryLogs.Select(entry =>
                $"{entry.Message} {string.Join(" ", entry.Attributes.Select(attribute => $"{attribute.Key}={attribute.Value}"))}"));
        Assert.DoesNotContain(secretGuid.ToString(), serializedLogs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-access-token", serializedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain("private-refresh-token", serializedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain("private-reason", serializedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain("TraceId", serializedLogs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SpanId", serializedLogs, StringComparison.OrdinalIgnoreCase);

        Assert.NotEmpty(exportedLogs);
        Assert.All(exportedLogs, record =>
        {
            Assert.Equal(
                ApplicationEventMetricsLoggerProvider.TelemetryCategory,
                record.CategoryName);
            Assert.Equal(default, record.TraceId);
            Assert.Equal(default, record.SpanId);
            Assert.Null(record.Exception);
            Assert.NotEqual(0, record.EventId.Id);
            Assert.False(string.IsNullOrWhiteSpace(record.EventId.Name));
            var attributes = LogAttributes(record);
            Assert.Equal(ApplicationTelemetryOptions.DefaultServiceName, attributes["service.name"]);
            Assert.Equal("test-version", attributes["service.version"]);
            Assert.Equal("testing", attributes["deployment.environment.name"]);
            Assert.True(attributes.Keys.All(AllowedEventAttributes.Contains));
            if (attributes.TryGetValue("reason", out var reason))
            {
                Assert.Contains(reason, new object?[] { "invalid_endpoint", "invalid_protocol" });
            }
        });
        var serializedExportedLogs = string.Join(
            " ",
            exportedLogs.Select(record =>
                $"{record.Body} {record.FormattedMessage} {string.Join(" ", LogAttributes(record).Select(attribute => $"{attribute.Key}={attribute.Value}"))}"));
        Assert.DoesNotContain(secretGuid.ToString(), serializedExportedLogs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", serializedExportedLogs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IdentityAudit", serializedExportedLogs, StringComparison.Ordinal);

        var runtimeMetrics = exportedMetrics
            .Where(metric => metric.MeterName == "System.Runtime")
            .ToArray();
        Assert.Equal(
            TelemetryServiceCollectionExtensions.RuntimeMetricTags.Keys.Order(),
            runtimeMetrics.Select(metric => metric.Name).Order());
        Assert.All(runtimeMetrics, metric =>
        {
            var allowedTags = TelemetryServiceCollectionExtensions.RuntimeMetricTags[metric.Name];
            Assert.All(
                SnapshotTagSets(metric),
                point => Assert.True(point.Tags.Keys.All(allowedTags.Contains)));
        });
        AssertRuntimeTagValues(runtimeMetrics);
    }

    [Fact]
    public async Task Unreachable_otlp_collector_does_not_affect_requests_or_health()
    {
        var logs = new CapturingLoggerProvider();
        var configuration = new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://127.0.0.1:1",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "grpc",
            ["OTEL_SERVICE_VERSION"] = "unavailable-exporter-test",
        };

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration(settings =>
                    settings.AddInMemoryCollection(configuration));
                builder.ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddProvider(logs);
                });
            });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");
        using var householdOperation = await client.PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, householdOperation.StatusCode);
        var requestLog = Assert.Single(
            logs.Entries,
            entry => entry.EventId.Name == "HttpRequestCompleted"
                && Equals(entry.Attributes["http.route"], "/health/live"));
        Assert.Equal("/health/live", requestLog.Attributes["http.route"]);
        var provider = factory.Services.GetRequiredService<MeterProvider>();
        _ = provider.ForceFlush(1_000);
    }

    [Fact]
    public async Task Escaping_exception_emits_failure_without_false_completion()
    {
        var logs = new CapturingLoggerProvider();
        await using var app = await StartTestApiAsync(logs);
        using var client = app.GetTestClient();

        var exception = await Record.ExceptionAsync(async () =>
        {
            using var response = await client.GetAsync("/response-started-failure");
            _ = await response.Content.ReadAsStringAsync();
        });

        Assert.NotNull(exception);
        var safeEvents = logs.Entries
            .Where(entry => entry.Category == ApplicationEventMetricsLoggerProvider.TelemetryCategory)
            .ToArray();
        Assert.Single(
            safeEvents,
            entry => entry.EventId.Name == "UnhandledRequestFailure"
                && Equals(entry.Attributes["http.route"], "/response-started-failure"));
        Assert.DoesNotContain(
            safeEvents,
            entry => entry.EventId.Name == "HttpRequestCompleted"
                && Equals(entry.Attributes["http.route"], "/response-started-failure"));
    }

    [Theory]
    [InlineData(99, "other")]
    [InlineData(200, "2xx")]
    [InlineData(304, "3xx")]
    [InlineData(401, "4xx")]
    [InlineData(503, "5xx")]
    [InlineData(600, "other")]
    public void Status_classification_is_bounded(int statusCode, string expected) =>
        Assert.Equal(expected, TelemetryDimensions.StatusClass(statusCode));

    [Fact]
    public void Invalid_exporter_configuration_is_disabled_without_exposing_its_value()
    {
        var invalidValue = "https://username:secret@example.test/v1/metrics?token=private";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = invalidValue,
                ["OTEL_SERVICE_NAME"] = "invalid service name containing spaces",
            })
            .Build();
        var environment = new TestHostEnvironment();

        var options = ApplicationTelemetryOptions.FromConfiguration(configuration, environment);

        Assert.Null(options.MetricsExport);
        Assert.Null(options.LogsExport);
        Assert.Equal("invalid_endpoint", options.ConfigurationIssue);
        Assert.Equal(ApplicationTelemetryOptions.DefaultServiceName, options.ServiceName);
        Assert.DoesNotContain(invalidValue, options.ConfigurationIssue, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development", "development")]
    [InlineData("local", "local")]
    [InlineData("PRODUCTION", "production")]
    [InlineData(" staging ", "staging")]
    [InlineData("test", "test")]
    [InlineData("Testing", "testing")]
    public void Deployment_environment_is_limited_to_the_shared_finite_vocabulary(
        string configuredValue,
        string expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telemetry:Environment"] = configuredValue,
            })
            .Build();

        var options = ApplicationTelemetryOptions.FromConfiguration(
            configuration,
            new TestHostEnvironment());

        Assert.Equal(expected, options.DeploymentEnvironment);
    }

    [Fact]
    public void Private_or_high_cardinality_deployment_environment_is_not_exported()
    {
        const string privateValue = "household-smith-2026-10-08";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telemetry:Environment"] = privateValue,
            })
            .Build();

        var options = ApplicationTelemetryOptions.FromConfiguration(
            configuration,
            new TestHostEnvironment { EnvironmentName = "custom-private-host" });

        Assert.Equal(
            ApplicationTelemetryOptions.DefaultDeploymentEnvironment,
            options.DeploymentEnvironment);
        Assert.DoesNotContain(privateValue, options.DeploymentEnvironment, StringComparison.Ordinal);
    }

    [Fact]
    public void Common_http_protobuf_endpoint_appends_signal_paths()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://alloy:4318/collector",
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            })
            .Build();

        var options = ApplicationTelemetryOptions.FromConfiguration(
            configuration,
            new TestHostEnvironment());

        Assert.Equal("http://alloy:4318/collector/v1/metrics", options.MetricsExport?.Endpoint.ToString());
        Assert.Equal("http://alloy:4318/collector/v1/logs", options.LogsExport?.Endpoint.ToString());
        Assert.Equal(OtlpExportProtocol.HttpProtobuf, options.MetricsExport?.Protocol);
        Assert.Equal(OtlpExportProtocol.HttpProtobuf, options.LogsExport?.Protocol);
    }

    [Fact]
    public void Signal_specific_http_endpoints_are_used_without_path_rewriting()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://ignored:4318",
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
                ["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"] = "http://metrics:4318/custom-metrics",
                ["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"] = "http://logs:4318/custom-logs",
            })
            .Build();

        var options = ApplicationTelemetryOptions.FromConfiguration(
            configuration,
            new TestHostEnvironment());

        Assert.Equal("http://metrics:4318/custom-metrics", options.MetricsExport?.Endpoint.ToString());
        Assert.Equal("http://logs:4318/custom-logs", options.LogsExport?.Endpoint.ToString());
    }

    [Fact]
    public async Task Exported_metric_and_log_resources_ignore_hostile_environment_attributes()
    {
        const string hostileAttributes =
            "service.instance.id=private-instance,container.id=private-container,host.name=private-host,household.name=private-family";
        var originalAttributes = Environment.GetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES");
        Environment.SetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES", hostileAttributes);

        try
        {
            var logs = new CapturingLoggerProvider();
            var exportedLogs = new List<LogRecord>();
            await using var app = BuildTestApi(
                logs,
                exportedLogs,
                new Dictionary<string, string?>
                {
                    ["OTEL_SERVICE_VERSION"] = "test-version",
                });

            await app.StartAsync();

            var metricResource = app.Services
                .GetRequiredService<MeterProvider>()
                .GetResource();
            var logResource = app.Services
                .GetServices<ILoggerProvider>()
                .OfType<OpenTelemetryLoggerProvider>()
                .Single()
                .GetResource();

            AssertAllowlistedResource(metricResource.Attributes);
            AssertAllowlistedResource(logResource.Attributes);
            Assert.Single(
                exportedLogs,
                record => record.EventId.Name == "ApplicationStarted");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES", originalAttributes);
        }
    }

    [Fact]
    public async Task Application_started_is_emitted_only_after_successful_host_start()
    {
        var successfulLogs = new CapturingLoggerProvider();
        await using (var successfulApp = BuildTestApi(successfulLogs))
        {
            Assert.DoesNotContain(
                successfulLogs.Entries,
                entry => entry.EventId.Name == "ApplicationStarted");

            await successfulApp.StartAsync();

            Assert.Single(
                successfulLogs.Entries,
                entry => entry.EventId.Name == "ApplicationStarted");
        }

        var failedLogs = new CapturingLoggerProvider();
        await using var failedApp = BuildTestApi(
            failedLogs,
            configureServices: services => services.AddHostedService<FailingHostedService>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => failedApp.StartAsync());
        Assert.DoesNotContain(
            failedLogs.Entries,
            entry => entry.EventId.Name == "ApplicationStarted");
    }

    [Fact]
    public async Task Failure_in_middleware_before_routing_is_sanitized_exactly_once()
    {
        var logs = new CapturingLoggerProvider();
        await using var app = await StartTestApiAsync(
            logs,
            configurePipeline: application => application.Use(
                (HttpContext _, RequestDelegate _) =>
                    throw new InvalidOperationException("private forwarded-header failure")));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/items/not-routed?token=private-token");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("private", body, StringComparison.OrdinalIgnoreCase);
        Assert.Single(
            logs.Entries,
            entry => entry.EventId.Name == "UnhandledRequestFailure");
        Assert.Single(
            logs.Entries,
            entry => entry.EventId.Name == "HttpRequestCompleted"
                && Equals(entry.Attributes["http.response.status_code"], 500));
    }

    private static async Task<WebApplication> StartTestApiAsync(
        CapturingLoggerProvider logs,
        ICollection<LogRecord>? exportedLogs = null,
        IReadOnlyDictionary<string, string?>? configuration = null,
        Action<IApplicationBuilder>? configurePipeline = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var app = BuildTestApi(
            logs,
            exportedLogs,
            configuration,
            configurePipeline,
            configureServices);
        await app.StartAsync();
        return app;
    }

    private static WebApplication BuildTestApi(
        CapturingLoggerProvider logs,
        ICollection<LogRecord>? exportedLogs = null,
        IReadOnlyDictionary<string, string?>? configuration = null,
        Action<IApplicationBuilder>? configurePipeline = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.Configure(options =>
            options.ActivityTrackingOptions = ActivityTrackingOptions.None);
        builder.Logging.AddProvider(logs);
        if (configuration is not null)
        {
            builder.Configuration.AddInMemoryCollection(configuration);
        }

        if (exportedLogs is not null)
        {
            var telemetryOptions = ApplicationTelemetryOptions.FromConfiguration(
                builder.Configuration,
                builder.Environment);
            TelemetryServiceCollectionExtensions.ConfigureSafeEventLogging(
                builder.Logging,
                telemetryOptions,
                loggerOptions => loggerOptions.AddInMemoryExporter(exportedLogs));
        }

        builder.Services.AddProblemDetails();
        builder.Services.AddApplicationTelemetry(builder.Configuration, builder.Environment);
        configureServices?.Invoke(builder.Services);
        var app = builder.Build();
        app.UseMiddleware<RequestTelemetryMiddleware>();
        app.UseExceptionHandler();
        configurePipeline?.Invoke(app);
        app.UseRouting();
        app.MapGet("/items/{itemId:guid}", () => Results.NoContent());
        app.MapPost("/api/auth/refresh", () => Results.Unauthorized());
        app.MapGet(
            "/controlled-warning",
            (TelemetryEventWriter events) =>
            {
                events.TelemetryConfigurationIgnored("invalid_protocol");
                return Results.NoContent();
            });
        app.MapGet(
            "/application-events",
            (ILoggerFactory loggerFactory) =>
            {
                loggerFactory.CreateLogger("FamilyJobsBoard.Api.Synthetic").LogWarning(
                    "Synthetic warning with secret {Secret}",
                    "private-warning-token");
                loggerFactory.CreateLogger("FamilyJobsBoard.Api.Synthetic").LogError(
                    "Synthetic error with secret {Secret}",
                    "private-error-token");
                loggerFactory.CreateLogger(ApplicationEventMetricsLoggerProvider.IdentityAuditCategory)
                    .LogWarning(
                        "SignInRejected MemberId={MemberId} Outcome={Outcome}",
                        Guid.NewGuid(),
                        "private-audit-outcome");
                return Results.NoContent();
            });
        app.MapGet(
            "/controlled-failure/{itemId:guid}",
            (Guid itemId) => Task.FromException<IResult>(
                new InvalidOperationException(
                    $"private-reason item={itemId:D} token=private-access-token")));
        app.MapGet(
            "/response-started-failure",
            async (HttpContext context) =>
            {
                await context.Response.WriteAsync("partial");
                throw new InvalidOperationException("private handler failure");
            });
        app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
        return app;
    }

    private static void AssertAllowlistedResource(
        IEnumerable<KeyValuePair<string, object>> resourceAttributes)
    {
        var attributes = resourceAttributes.ToDictionary(
            attribute => attribute.Key,
            attribute => attribute.Value,
            StringComparer.Ordinal);

        Assert.Equal(
            new[]
            {
                "deployment.environment.name",
                "service.name",
                "service.version",
            },
            attributes.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(ApplicationTelemetryOptions.DefaultServiceName, attributes["service.name"]);
        Assert.Equal("test-version", attributes["service.version"]);
        Assert.Equal("testing", attributes["deployment.environment.name"]);
    }

    private static ApplicationTelemetryOptions TestOptions() => new(
        ApplicationTelemetryOptions.DefaultServiceName,
        "test-version",
        "testing",
        null,
        null,
        null);

    private static IReadOnlyList<MetricPointSnapshot> SnapshotPoints(
        Metric metric,
        bool histogram)
    {
        var snapshots = new List<MetricPointSnapshot>();
        foreach (ref readonly var point in metric.GetMetricPoints())
        {
            var tags = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var tag in point.Tags)
            {
                tags.Add(tag.Key, tag.Value?.ToString() ?? string.Empty);
            }
            snapshots.Add(new(
                tags,
                histogram ? checked((long)point.GetHistogramCount()) : point.GetSumLong()));
        }

        return snapshots;
    }

    private static IReadOnlyList<MetricPointSnapshot> SnapshotTagSets(Metric metric)
    {
        var snapshots = new List<MetricPointSnapshot>();
        foreach (ref readonly var point in metric.GetMetricPoints())
        {
            var tags = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var tag in point.Tags)
            {
                tags.Add(tag.Key, tag.Value?.ToString() ?? string.Empty);
            }

            snapshots.Add(new(tags, 0));
        }

        return snapshots;
    }

    private static IReadOnlyDictionary<string, object?> LogAttributes(LogRecord record) =>
        record.Attributes?.ToDictionary(
            attribute => attribute.Key,
            attribute => attribute.Value,
            StringComparer.Ordinal)
        ?? new Dictionary<string, object?>(StringComparer.Ordinal);

    private static void AssertRuntimeTagValues(IEnumerable<Metric> runtimeMetrics)
    {
        var points = runtimeMetrics.ToDictionary(
            metric => metric.Name,
            SnapshotTagSets,
            StringComparer.Ordinal);
        var generations = points
            .Where(metric => TelemetryServiceCollectionExtensions.RuntimeMetricTags[metric.Key]
                .Contains("gc.heap.generation", StringComparer.Ordinal))
            .SelectMany(metric => metric.Value)
            .Select(point => Tag(point, "gc.heap.generation"))
            .OfType<string>()
            .ToArray();
        Assert.NotEmpty(generations);
        Assert.All(generations, generation => Assert.Contains(generation, AllowedGcGenerations));

        var cpuModes = points["dotnet.process.cpu.time"]
            .Select(point => Tag(point, "cpu.mode"))
            .OfType<string>()
            .ToArray();
        Assert.NotEmpty(cpuModes);
        Assert.All(cpuModes, mode => Assert.Contains(mode, AllowedCpuModes));
    }

    private static long SumCounter(IEnumerable<Metric> metrics, string name)
    {
        var metric = Assert.Single(metrics, candidate => candidate.Name == name);
        return SnapshotPoints(metric, histogram: false).Sum(point => point.Count);
    }

    private static string? Tag(MetricPointSnapshot point, string name) =>
        point.Tags.GetValueOrDefault(name);

    private sealed record MetricPointSnapshot(
        IReadOnlyDictionary<string, string> Tags,
        long Count);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

        public IReadOnlyCollection<CapturedLogEntry> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class CaptureLogger(
            CapturingLoggerProvider provider,
            string categoryName) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
                if (state is IEnumerable<KeyValuePair<string, object?>> stateValues)
                {
                    foreach (var value in stateValues)
                    {
                        attributes[value.Key] = value.Value;
                    }
                }

                provider._entries.Enqueue(new(
                    categoryName,
                    logLevel,
                    eventId,
                    formatter(state, exception),
                    exception,
                    attributes));
            }
        }
    }

    private sealed record CapturedLogEntry(
        string Category,
        LogLevel Level,
        EventId EventId,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Attributes);

    private sealed class FailingHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("Host failed to start."));

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "FamilyJobsBoard.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TelemetryTestCollection
{
    public const string Name = "Application telemetry tests";
}
