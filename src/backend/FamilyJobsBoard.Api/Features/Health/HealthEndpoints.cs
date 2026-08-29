using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FamilyJobsBoard.Api.Features.Health;

internal static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => TypedResults.Ok(new HealthResponse("healthy")))
            .ExcludeFromDescription();

        endpoints.MapGet(
                "/health/ready",
                async Task<Results<Ok<HealthResponse>, StatusCodeHttpResult>> (
                    AppDbContext database,
                    CancellationToken cancellationToken) =>
                {
                    var canConnect = await database.Database.CanConnectAsync(cancellationToken);
                    return canConnect
                        ? TypedResults.Ok(new HealthResponse("healthy"))
                        : TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
                })
            .ExcludeFromDescription();

        return endpoints;
    }

    private sealed record HealthResponse(string Status);
}
