using System.Diagnostics;

namespace FamilyJobsBoard.Api.Features.Telemetry;

internal sealed class RequestTelemetryMiddleware(
    RequestDelegate next,
    ApplicationTelemetry telemetry,
    TelemetryEventWriter events)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var method = TelemetryDimensions.Method(context.Request.Method);
        var route = TelemetryDimensions.Route(context);
        var requestCompleted = true;

        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            requestCompleted = false;
            throw;
        }
        catch (Exception)
        {
            requestCompleted = false;
            route = TelemetryDimensions.Route(context);
            events.UnhandledRequestFailure(method, route);
            throw;
        }
        finally
        {
            if (requestCompleted)
            {
                route = TelemetryDimensions.Route(context);
                var statusCode = context.Response.StatusCode;
                if (TelemetryDimensions.IsAuthenticationRejection(route, statusCode))
                {
                    telemetry.RecordAuthenticationRejection(method, route, statusCode);
                    events.AuthenticationRejected(method, route, statusCode);
                }

                events.RequestCompleted(
                    method,
                    route,
                    statusCode,
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            }
        }
    }
}
