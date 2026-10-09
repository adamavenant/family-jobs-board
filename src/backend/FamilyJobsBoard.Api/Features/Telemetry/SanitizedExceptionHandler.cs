using System.Text;
using Microsoft.AspNetCore.Diagnostics;

namespace FamilyJobsBoard.Api.Features.Telemetry;

internal sealed class SanitizedExceptionHandler(TelemetryEventWriter events) : IExceptionHandler
{
    private static readonly byte[] InternalServerError = Encoding.UTF8.GetBytes(
        "{\"type\":\"about:blank\",\"title\":\"An unexpected error occurred.\",\"status\":500,\"detail\":\"The request could not be completed.\"}");

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException
            && httpContext.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        var method = TelemetryDimensions.Method(httpContext.Request.Method);
        var route = TelemetryDimensions.Route(httpContext);
        events.UnhandledRequestFailure(method, route);

        httpContext.Response.Clear();
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/problem+json";
        httpContext.Response.ContentLength = InternalServerError.Length;
        await httpContext.Response.Body.WriteAsync(InternalServerError, cancellationToken);
        return true;
    }
}
