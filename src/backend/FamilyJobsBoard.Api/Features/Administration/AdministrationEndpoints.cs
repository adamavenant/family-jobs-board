using FamilyJobsBoard.Api.Features.Identity;
using FamilyJobsBoard.Application.Administration;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FamilyJobsBoard.Api.Features.Administration;

internal static class AdministrationEndpoints
{
    public static IEndpointRouteBuilder MapAdministrationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin")
            .WithTags("Administration")
            .RequireAuthorization("Adult");

        group.MapPost("/jobs-and-points/reset", ResetJobsAndPointsAsync)
            .WithName("ResetJobsAndPoints")
            .WithSummary("Irreversibly clear household job and points data.")
            .WithDescription(
                $"Deletes jobs, recurring series, review decisions, and points entries in one transaction. Requires the exact confirmation phrase '{AdministrationService.RequiredConfirmation}'. User profiles and credentials are preserved.");

        return endpoints;
    }

    private static async Task<Results<Ok<ResetJobsAndPointsResponse>, ValidationProblem>>
        ResetJobsAndPointsAsync(
            ResetJobsAndPointsRequest request,
            HttpContext context,
            AdministrationService service,
            CancellationToken cancellationToken)
    {
        try
        {
            var reset = await service.ResetJobsAndPointsAsync(
                IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                request.Confirmation,
                cancellationToken);
            return TypedResults.Ok(new ResetJobsAndPointsResponse(
                reset.ResetId,
                reset.OccurredAtUtc,
                reset.DeletedJobCount,
                reset.DeletedRecurringSeriesCount,
                reset.DeletedReviewDecisionCount,
                reset.DeletedPointsEntryCount));
        }
        catch (InvalidAdminDataResetConfirmationException exception)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [nameof(ResetJobsAndPointsRequest.Confirmation)] = [exception.Message],
                },
                title: "Reset confirmation required");
        }
    }
}
