using FamilyJobsBoard.Api.Features.Identity;
using FamilyJobsBoard.Application.PointAdjustments;
using FamilyJobsBoard.Application.Points;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FamilyJobsBoard.Api.Features.PointAdjustments;

internal static class PointAdjustmentEndpoints
{
    public const string InsufficientPointsCode = "insufficientPoints";
    public const string RequestConflictCode = "requestConflict";

    public static IEndpointRouteBuilder MapPointAdjustmentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api")
            .WithTags("Point adjustments")
            .RequireAuthorization("Adult");

        group.MapPost("/point-adjustments", RecordAsync)
            .WithName("RecordPointAdjustment")
            .WithSummary("Manually add or remove points for a child.")
            .WithDescription(
                "Adult-only. Records a signed, non-zero adjustment with a required reason as its own append-only ledger entry; mistakes are corrected with a new opposite adjustment. Points are never taken below zero: a removal larger than the balance returns 409 with code 'insufficientPoints' and the current balance. Repeating a request ID with the same details returns the original result without a second entry; reusing it for different details returns 409 with code 'requestConflict'.");

        return endpoints;
    }

    private static async Task<Results<
        Created<RecordPointAdjustmentResponse>,
        Ok<RecordPointAdjustmentResponse>,
        ValidationProblem,
        Conflict<ProblemDetails>>> RecordAsync(
        RecordPointAdjustmentRequest request,
        HttpContext context,
        PointAdjustmentService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.RecordAsync(
                new RecordPointAdjustment(
                    request.RequestId,
                    IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                    request.ChildId,
                    request.Amount,
                    request.Reason),
                cancellationToken);
            var adjustment = result.Adjustment;
            var response = new RecordPointAdjustmentResponse(
                new PointAdjustmentResponse(
                    adjustment.Id,
                    adjustment.ChildId,
                    adjustment.AdjustedByMemberId,
                    adjustment.Amount,
                    adjustment.Reason,
                    adjustment.AdjustedAtUtc),
                result.PointsBalance);
            return result.WasCreated
                ? TypedResults.Created($"/api/point-adjustments/{request.RequestId}", response)
                : TypedResults.Ok(response);
        }
        catch (InvalidPointAdjustmentException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors,
                title: "Invalid point adjustment data");
        }
        catch (InsufficientPointsException exception)
        {
            var problem = new ProblemDetails
            {
                Title = "Not enough points",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict,
            };
            problem.Extensions["code"] = InsufficientPointsCode;
            problem.Extensions["currentBalance"] = exception.CurrentBalance;
            return TypedResults.Conflict(problem);
        }
        catch (PointAdjustmentRequestConflictException exception)
        {
            var problem = new ProblemDetails
            {
                Title = "Point adjustment request conflict",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict,
            };
            problem.Extensions["code"] = RequestConflictCode;
            return TypedResults.Conflict(problem);
        }
    }
}
