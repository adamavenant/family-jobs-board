using FamilyJobsBoard.Api.Features.Identity;
using FamilyJobsBoard.Application.TurnRotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FamilyJobsBoard.Api.Features.TurnRotations;

internal static class TurnRotationEndpoints
{
    public static IEndpointRouteBuilder MapTurnRotationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api")
            .WithTags("Whose turn")
            .RequireAuthorization("Adult");

        group.MapGet("/turn-rotation", GetOverviewAsync)
            .WithName("GetTurnRotation")
            .WithSummary("Get the household's whose-turn rotation configuration.")
            .WithDescription(
                "Adult-only. Returns the revision effective today, if any, and a short preview of upcoming turns.");

        group.MapPut("/turn-rotation", SaveAsync)
            .WithName("SaveTurnRotation")
            .WithSummary("Create a new effective-dated whose-turn rotation revision.")
            .WithDescription(
                "Adult-only. Existing revisions are immutable; this always creates a new one, effective today for the first-ever configuration or no earlier than tomorrow otherwise.");

        return endpoints;
    }

    private static async Task<Ok<TurnRotationOverviewResponse>> GetOverviewAsync(
        TurnRotationService service,
        CancellationToken cancellationToken)
    {
        var overview = await service.GetOverviewAsync(cancellationToken);
        return TypedResults.Ok(MapOverview(overview));
    }

    private static async Task<Results<
        Ok<TurnRotationOverviewResponse>,
        ValidationProblem,
        ForbidHttpResult>> SaveAsync(
        SaveTurnRotationRequest request,
        HttpContext context,
        TurnRotationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var overview = await service.SaveAsync(
                IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                new SaveTurnRotation(
                    request.ParticipantChildIds,
                    request.FirstChildId,
                    request.EffectiveFrom,
                    request.Question),
                cancellationToken);
            return TypedResults.Ok(MapOverview(overview));
        }
        catch (InvalidTurnRotationException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors,
                title: "Invalid whose-turn rotation data");
        }
        catch (TurnRotationForbiddenException)
        {
            return TypedResults.Forbid();
        }
    }

    private static TurnRotationOverviewResponse MapOverview(TurnRotationOverview overview)
    {
        return new TurnRotationOverviewResponse(
            overview.Current is null ? null : MapConfiguration(overview.Current),
            overview.UpcomingTurns
                .Select(turn => new TurnRotationTurnResponse(
                    turn.Date, turn.Question, turn.ChildId, turn.ChildDisplayName))
                .ToArray(),
            overview.HasRevisions);
    }

    private static TurnRotationConfigurationResponse MapConfiguration(TurnRotationConfiguration configuration)
    {
        return new TurnRotationConfigurationResponse(
            configuration.Id,
            configuration.EffectiveFrom,
            configuration.Question,
            configuration.ParticipantChildIds,
            configuration.FirstChildId,
            configuration.CreatedByMemberId,
            configuration.CreatedAtUtc);
    }
}
