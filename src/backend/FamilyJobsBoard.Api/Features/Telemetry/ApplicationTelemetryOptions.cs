using System.Reflection;
using OpenTelemetry.Exporter;

namespace FamilyJobsBoard.Api.Features.Telemetry;

internal sealed record OtlpSignalExportOptions(
    Uri Endpoint,
    OtlpExportProtocol Protocol);

internal sealed record ApplicationTelemetryOptions(
    string ServiceName,
    string ServiceVersion,
    string DeploymentEnvironment,
    OtlpSignalExportOptions? MetricsExport,
    OtlpSignalExportOptions? LogsExport,
    string? ConfigurationIssue)
{
    internal const string DefaultServiceName = "family-jobs-board-api";
    internal const string DefaultDeploymentEnvironment = "production";

    private static readonly HashSet<string> AllowedDeploymentEnvironments =
    [
        "development",
        "local",
        "production",
        "staging",
        "test",
        "testing",
    ];

    public static ApplicationTelemetryOptions FromConfiguration(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var serviceName = NormalizeResourceValue(
            configuration["OTEL_SERVICE_NAME"],
            DefaultServiceName);
        var assemblyVersion = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+', 2)[0]
            ?? "unknown";
        var serviceVersion = NormalizeResourceValue(
            configuration["OTEL_SERVICE_VERSION"],
            assemblyVersion);
        var deploymentEnvironment = NormalizeDeploymentEnvironment(
            configuration["Telemetry:Environment"],
            environment.EnvironmentName);

        if (!TryCreateSignalExport(
                configuration,
                "METRICS",
                "v1/metrics",
                out var metricsExport,
                out var configurationIssue)
            || !TryCreateSignalExport(
                configuration,
                "LOGS",
                "v1/logs",
                out var logsExport,
                out configurationIssue))
        {
            return new(
                serviceName,
                serviceVersion,
                deploymentEnvironment,
                null,
                null,
                configurationIssue);
        }

        return new(
            serviceName,
            serviceVersion,
            deploymentEnvironment,
            metricsExport,
            logsExport,
            null);
    }

    private static bool TryCreateSignalExport(
        IConfiguration configuration,
        string signalName,
        string httpSignalPath,
        out OtlpSignalExportOptions? signalExport,
        out string? configurationIssue)
    {
        signalExport = null;
        configurationIssue = null;

        var signalEndpointValue = NonWhitespace(
            configuration[$"OTEL_EXPORTER_OTLP_{signalName}_ENDPOINT"]);
        var commonEndpointValue = NonWhitespace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        var endpointValue = signalEndpointValue ?? commonEndpointValue;
        if (endpointValue is null)
        {
            return true;
        }

        if (!TryParseEndpoint(endpointValue, out var endpoint))
        {
            configurationIssue = "invalid_endpoint";
            return false;
        }

        var protocolValue = NonWhitespace(
                configuration[$"OTEL_EXPORTER_OTLP_{signalName}_PROTOCOL"])
            ?? NonWhitespace(configuration["OTEL_EXPORTER_OTLP_PROTOCOL"])
            ?? "grpc";
        var protocol = protocolValue switch
        {
            "grpc" => OtlpExportProtocol.Grpc,
            "http/protobuf" => OtlpExportProtocol.HttpProtobuf,
            _ => (OtlpExportProtocol?)null,
        };
        if (protocol is null)
        {
            configurationIssue = "invalid_protocol";
            return false;
        }

        if (protocol == OtlpExportProtocol.HttpProtobuf
            && signalEndpointValue is null)
        {
            endpoint = AppendSignalPath(endpoint, httpSignalPath);
        }

        signalExport = new(endpoint, protocol.Value);
        return true;
    }

    private static bool TryParseEndpoint(string value, out Uri endpoint)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(parsed.UserInfo)
            && string.IsNullOrEmpty(parsed.Query)
            && string.IsNullOrEmpty(parsed.Fragment))
        {
            endpoint = parsed;
            return true;
        }

        endpoint = null!;
        return false;
    }

    private static string? NonWhitespace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static Uri AppendSignalPath(Uri endpoint, string signalPath)
    {
        var builder = new UriBuilder(endpoint)
        {
            Path = $"{endpoint.AbsolutePath.TrimEnd('/')}/{signalPath}",
        };
        return builder.Uri;
    }

    private static string NormalizeResourceValue(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 128
            && trimmed.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_' or '.')
            ? trimmed
            : fallback;
    }

    private static string NormalizeDeploymentEnvironment(string? value, string hostEnvironment)
    {
        var fallback = TryNormalizeDeploymentEnvironment(hostEnvironment)
            ?? DefaultDeploymentEnvironment;
        return TryNormalizeDeploymentEnvironment(value) ?? fallback;
    }

    private static string? TryNormalizeDeploymentEnvironment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return AllowedDeploymentEnvironments.Contains(normalized)
            ? normalized
            : null;
    }
}
