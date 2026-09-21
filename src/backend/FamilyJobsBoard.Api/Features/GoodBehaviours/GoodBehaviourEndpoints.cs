using FamilyJobsBoard.Api.Features.Identity;
using FamilyJobsBoard.Application.GoodBehaviours;
using FamilyJobsBoard.Domain.GoodBehaviours;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FamilyJobsBoard.Api.Features.GoodBehaviours;

internal static class GoodBehaviourEndpoints
{
    public static IEndpointRouteBuilder MapGoodBehaviourEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").WithTags("Good behaviours").RequireAuthorization();

        group.MapGet("/good-behaviour-types", ListTypesAsync)
            .WithName("ListGoodBehaviourTypes")
            .WithSummary("List the good behaviour types adults can log.")
            .WithDescription(
                "Returns active types with the points each usually earns. Any signed-in family member may read this list; the amount awarded for a logged behaviour can differ.");

        group.MapPost("/good-behaviour-types", CreateTypeAsync)
            .RequireAuthorization("Adult")
            .WithName("CreateGoodBehaviourType")
            .WithSummary("Create a good behaviour type.")
            .WithDescription("Adult-only. The type is immediately available to log for any active child.");

        group.MapPut("/good-behaviour-types/{id:guid}", UpdateTypeAsync)
            .RequireAuthorization("Adult")
            .WithName("UpdateGoodBehaviourType")
            .WithSummary("Edit a good behaviour type.")
            .WithDescription(
                "Adult-only. Previously logged behaviours keep the name, description, and points they were logged with. Returns 409 for a deleted type.");

        group.MapDelete("/good-behaviour-types/{id:guid}", DeleteTypeAsync)
            .RequireAuthorization("Adult")
            .WithName("DeleteGoodBehaviourType")
            .WithSummary("Delete a good behaviour type.")
            .WithDescription(
                "Adult-only soft delete: the type can no longer be logged, and history is unchanged. Repeating the request succeeds.");

        group.MapPost("/good-behaviours", LogAsync)
            .RequireAuthorization("Adult")
            .WithName("LogGoodBehaviour")
            .WithSummary("Log a good behaviour for one or more children and award its points.")
            .WithDescription(
                "Adult-only. Creates an independent behaviour and points ledger entry for each selected child, all in one transaction. Points default to the type's points and apply to each child. Repeating a request ID with the same details returns the original result without a second award; reusing it for different details returns 409.");

        return endpoints;
    }

    private static async Task<Ok<GoodBehaviourTypesResponse>> ListTypesAsync(
        GoodBehaviourService service,
        CancellationToken cancellationToken)
    {
        var types = await service.ListTypesAsync(cancellationToken);
        return TypedResults.Ok(new GoodBehaviourTypesResponse(
            types.Select(MapType).ToArray()));
    }

    private static async Task<Results<
        Created<GoodBehaviourTypeResponse>,
        ValidationProblem>> CreateTypeAsync(
        SaveGoodBehaviourTypeRequest request,
        HttpContext context,
        GoodBehaviourService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var type = await service.CreateTypeAsync(
                IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                new SaveGoodBehaviourType(request.Name, request.Description, request.Points),
                cancellationToken);
            return TypedResults.Created($"/api/good-behaviour-types/{type.Id}", MapType(type));
        }
        catch (InvalidGoodBehaviourException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors,
                title: "Invalid good behaviour type data");
        }
    }

    private static async Task<Results<
        Ok<GoodBehaviourTypeResponse>,
        ValidationProblem,
        NotFound<ProblemDetails>,
        Conflict<ProblemDetails>>> UpdateTypeAsync(
        Guid id,
        SaveGoodBehaviourTypeRequest request,
        HttpContext context,
        GoodBehaviourService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var type = await service.UpdateTypeAsync(
                IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                id,
                new SaveGoodBehaviourType(request.Name, request.Description, request.Points),
                cancellationToken);
            return TypedResults.Ok(MapType(type));
        }
        catch (InvalidGoodBehaviourException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors,
                title: "Invalid good behaviour type data");
        }
        catch (GoodBehaviourTypeNotFoundException exception)
        {
            return TypeNotFound(exception);
        }
        catch (GoodBehaviourTypeInactiveException exception)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Good behaviour type was deleted",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict,
            });
        }
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>>> DeleteTypeAsync(
        Guid id,
        HttpContext context,
        GoodBehaviourService service,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.DeleteTypeAsync(
                IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                id,
                cancellationToken);
            return TypedResults.NoContent();
        }
        catch (GoodBehaviourTypeNotFoundException exception)
        {
            return TypeNotFound(exception);
        }
    }

    private static async Task<Results<
        Created<LogGoodBehaviourResponse>,
        Ok<LogGoodBehaviourResponse>,
        ValidationProblem,
        Conflict<ProblemDetails>>> LogAsync(
        LogGoodBehaviourRequest request,
        HttpContext context,
        GoodBehaviourService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.LogAsync(
                new LogGoodBehaviour(
                    request.RequestId,
                    IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                    request.TypeId,
                    request.ChildIds,
                    request.Points),
                cancellationToken);
            var response = new LogGoodBehaviourResponse(
                result.Awards
                    .Select(award => new GoodBehaviourAwardResponse(
                        MapBehaviour(award.Behaviour),
                        award.PointsBalance))
                    .ToArray());
            return result.WasCreated
                ? TypedResults.Created($"/api/good-behaviours/{request.RequestId}", response)
                : TypedResults.Ok(response);
        }
        catch (InvalidGoodBehaviourException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors,
                title: "Invalid good behaviour data");
        }
        catch (GoodBehaviourRequestConflictException exception)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Good behaviour request conflict",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict,
            });
        }
    }

    private static NotFound<ProblemDetails> TypeNotFound(GoodBehaviourTypeNotFoundException exception)
    {
        return TypedResults.NotFound(new ProblemDetails
        {
            Title = "Good behaviour type not found",
            Detail = exception.Message,
            Status = StatusCodes.Status404NotFound,
        });
    }

    private static GoodBehaviourTypeResponse MapType(GoodBehaviourTypeSummary type) =>
        new(type.Id, type.Name, type.Description, type.Points);

    private static GoodBehaviourResponse MapBehaviour(LoggedGoodBehaviour behaviour) =>
        new(
            behaviour.Id,
            behaviour.TypeId,
            behaviour.TypeName,
            behaviour.TypeDescription,
            behaviour.ChildId,
            behaviour.LoggedByMemberId,
            behaviour.Points,
            behaviour.LoggedAtUtc);
}
