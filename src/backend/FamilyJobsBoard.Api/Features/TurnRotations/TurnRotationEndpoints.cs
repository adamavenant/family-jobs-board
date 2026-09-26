using FamilyJobsBoard.Api.Features.Identity;
using FamilyJobsBoard.Application.TurnRotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FamilyJobsBoard.Api.Features.TurnRotations;

internal static class TurnRotationEndpoints
{
    public static IEndpointRouteBuilder MapTurnRotationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/turn-rotations")
            .WithTags("Whose turn")
            .RequireAuthorization("Adult");

        group.MapGet("", GetOverviewAsync)
            .WithName("ListTurnRotations")
            .WithSummary("List the household's whose-turn rotations.")
            .WithDescription(
                "Adult-only. Returns every active rotation with the revision effective today, if any, and a short preview of upcoming turns.");

        group.MapPost("", CreateAsync)
            .WithName("CreateTurnRotation")
            .WithSummary("Start a new whose-turn rotation.")
            .WithDescription(
                "Adult-only. Creates the rotation's first revision, which may be effective today or later.");

        group.MapPut("/{rotationId:guid}", UpdateAsync)
            .WithName("UpdateTurnRotation")
            .WithSummary("Create a new effective-dated revision of a rotation.")
            .WithDescription(
                "Adult-only. Existing revisions are immutable; this always adds one, effective no earlier than tomorrow.");

        group.MapDelete("/{rotationId:guid}", EndAsync)
            .WithName("EndTurnRotation")
            .WithSummary("End a rotation from tomorrow.")
            .WithDescription(
                "Adult-only. The rotation stops appearing on the board from tomorrow; earlier dates keep their answers.");

        return endpoints;
    }

    private static async Task<Ok<TurnRotationOverviewResponse>> GetOverviewAsync(
        TurnRotationService service,
        CancellationToken cancellationToken)
    {
        return TypedResults.Ok(MapOverview(await service.GetOverviewAsync(cancellationToken)));
    }

    private static Task<Results<Ok<TurnRotationOverviewResponse>, ValidationProblem, NotFound<ProblemDetails>, ForbidHttpResult>>
        CreateAsync(
            SaveTurnRotationRequest request,
            HttpContext context,
            TurnRotationService service,
            CancellationToken cancellationToken) =>
        SaveAsync(null, request, context, service, cancellationToken);

    private static Task<Results<Ok<TurnRotationOverviewResponse>, ValidationProblem, NotFound<ProblemDetails>, ForbidHttpResult>>
        UpdateAsync(
            Guid rotationId,
            SaveTurnRotationRequest request,
            HttpContext context,
            TurnRotationService service,
            CancellationToken cancellationToken) =>
        SaveAsync(rotationId, request, context, service, cancellationToken);

    private static async Task<Results<Ok<TurnRotationOverviewResponse>, ValidationProblem, NotFound<ProblemDetails>, ForbidHttpResult>>
        SaveAsync(
            Guid? rotationId,
            SaveTurnRotationRequest request,
            HttpContext context,
            TurnRotationService service,
            CancellationToken cancellationToken)
    {
        try
        {
            var overview = await service.SaveAsync(
                IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                rotationId,
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
        catch (TurnRotationNotFoundException exception)
        {
            return NotFound(exception);
        }
        catch (TurnRotationForbiddenException)
        {
            return TypedResults.Forbid();
        }
    }

    private static async Task<Results<Ok<TurnRotationOverviewResponse>, NotFound<ProblemDetails>, ForbidHttpResult>>
        EndAsync(
            Guid rotationId,
            HttpContext context,
            TurnRotationService service,
            CancellationToken cancellationToken)
    {
        try
        {
            var overview = await service.EndAsync(
                IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                rotationId,
                cancellationToken);
            return TypedResults.Ok(MapOverview(overview));
        }
        catch (TurnRotationNotFoundException exception)
        {
            return NotFound(exception);
        }
        catch (TurnRotationForbiddenException)
        {
            return TypedResults.Forbid();
        }
    }

    private static NotFound<ProblemDetails> NotFound(TurnRotationNotFoundException exception) =>
        TypedResults.NotFound(new ProblemDetails
        {
            Title = "Whose-turn rotation not found",
            Detail = exception.Message,
            Status = StatusCodes.Status404NotFound,
        });

    private static TurnRotationOverviewResponse MapOverview(TurnRotationOverview overview)
    {
        return new TurnRotationOverviewResponse(
            overview.Rotations
                .Select(rotation => new TurnRotationSummaryResponse(
                    rotation.RotationId,
                    rotation.Current is null ? null : MapConfiguration(rotation.Current),
                    rotation.UpcomingTurns
                        .Select(turn => new TurnRotationTurnResponse(
                            turn.Date, turn.Question, turn.ChildId, turn.ChildDisplayName))
                        .ToArray()))
                .ToArray());
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
