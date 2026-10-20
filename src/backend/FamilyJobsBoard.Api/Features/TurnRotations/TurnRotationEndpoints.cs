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

        var turns = endpoints.MapGroup("/api/turn-rotations/{rotationId:guid}/turns")
            .WithTags("Whose turn")
            .RequireAuthorization();

        turns.MapPost("", TakeTurnAsync)
            .WithName("TakeTurn")
            .WithSummary("Tick off today's turn in a rotation that moves on when ticked.")
            .WithDescription(
                "Any adult, or the child whose turn it is. The body names the turn from the board by revision, turn number, and child; a tick for a turn that has already moved on, or a second tick today in a rotation of one or three or more children, is a conflict. A two-child rotation moves straight on to the other child. Replaying a request ID returns the original tick without ticking again.");

        turns.MapDelete("/{turnId:guid}", UndoTurnAsync)
            .WithName("UndoTurn")
            .WithSummary("Untick the rotation's most recent tick.")
            .WithDescription(
                "Any adult, or the child whose turn was ticked. Only the rotation's most recent tick can be unticked, on the household-local day it was made; the tick is kept with who unticked it and when.");

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
                    request.Question,
                    request.AdvanceMode),
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

    private static async Task<Results<Ok<TakenTurnResponse>, ValidationProblem, NotFound<ProblemDetails>, Conflict<ProblemDetails>, ForbidHttpResult>>
        TakeTurnAsync(
            Guid rotationId,
            TakeTurnRequest request,
            HttpContext context,
            TurnRotationService service,
            CancellationToken cancellationToken)
    {
        try
        {
            var taken = await service.TakeTurnAsync(
                IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                rotationId,
                new TakeTurn(
                    request.RequestId ?? Guid.Empty,
                    request.RevisionId,
                    request.TurnNumber,
                    request.ChildId),
                cancellationToken);
            return TypedResults.Ok(MapTakenTurn(taken));
        }
        catch (InvalidTurnRotationException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors,
                title: "Invalid turn tick");
        }
        catch (TurnRotationNotFoundException exception)
        {
            return NotFound(exception);
        }
        catch (TakenTurnConflictException exception)
        {
            return Conflict(exception);
        }
        catch (TakenTurnForbiddenException)
        {
            return TypedResults.Forbid();
        }
    }

    private static async Task<Results<Ok<TakenTurnResponse>, NotFound<ProblemDetails>, Conflict<ProblemDetails>, ForbidHttpResult>>
        UndoTurnAsync(
            Guid rotationId,
            Guid turnId,
            HttpContext context,
            TurnRotationService service,
            CancellationToken cancellationToken)
    {
        try
        {
            var undone = await service.UndoTurnAsync(
                IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                rotationId,
                turnId,
                cancellationToken);
            return TypedResults.Ok(MapTakenTurn(undone));
        }
        catch (TakenTurnNotFoundException exception)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Taken turn not found",
                Detail = exception.Message,
                Status = StatusCodes.Status404NotFound,
            });
        }
        catch (TakenTurnConflictException exception)
        {
            return Conflict(exception);
        }
        catch (TakenTurnForbiddenException)
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

    private static Conflict<ProblemDetails> Conflict(TakenTurnConflictException exception) =>
        TypedResults.Conflict(new ProblemDetails
        {
            Title = "Turn cannot be changed",
            Detail = exception.Message,
            Status = StatusCodes.Status409Conflict,
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
                        .ToArray(),
                    rotation.UpNext
                        .Select(child => new TurnRotationChildResponse(child.ChildId, child.ChildDisplayName))
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
            configuration.AdvanceMode,
            configuration.CreatedByMemberId,
            configuration.CreatedAtUtc);
    }

    private static TakenTurnResponse MapTakenTurn(TakenTurnDetails taken)
    {
        return new TakenTurnResponse(
            taken.Id,
            taken.RequestId,
            taken.RotationId,
            taken.RevisionId,
            taken.TurnNumber,
            taken.Date,
            taken.ChildId,
            taken.ChildDisplayName,
            taken.RecordedByMemberId,
            taken.RecordedAtUtc,
            taken.UndoneByMemberId,
            taken.UndoneAtUtc);
    }
}
