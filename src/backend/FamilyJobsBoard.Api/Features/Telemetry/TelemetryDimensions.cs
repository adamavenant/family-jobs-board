using Microsoft.AspNetCore.Routing;

namespace FamilyJobsBoard.Api.Features.Telemetry;

internal static class TelemetryDimensions
{
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.Ordinal)
    {
        HttpMethods.Connect,
        HttpMethods.Delete,
        HttpMethods.Get,
        HttpMethods.Head,
        HttpMethods.Options,
        HttpMethods.Patch,
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Trace,
    };

    public static string Method(string? method)
    {
        var normalized = method?.ToUpperInvariant();
        return normalized is not null && AllowedMethods.Contains(normalized)
            ? normalized
            : "_OTHER";
    }

    public static string Route(HttpContext context)
    {
        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        return !string.IsNullOrWhiteSpace(route) && route.Length <= 160
            ? route
            : "_unmatched";
    }

    public static string StatusClass(int statusCode) => statusCode switch
    {
        >= 100 and <= 199 => "1xx",
        >= 200 and <= 299 => "2xx",
        >= 300 and <= 399 => "3xx",
        >= 400 and <= 499 => "4xx",
        >= 500 and <= 599 => "5xx",
        _ => "other",
    };

    public static bool IsAuthenticationRejection(
        string route,
        int statusCode) =>
        statusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden
        || (statusCode == StatusCodes.Status429TooManyRequests
            && route == "/api/auth/sign-in");
}
