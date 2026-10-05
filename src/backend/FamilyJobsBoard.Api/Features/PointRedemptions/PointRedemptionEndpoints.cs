using FamilyJobsBoard.Api.Features.Identity;
using FamilyJobsBoard.Application.PointRedemptions;
using FamilyJobsBoard.Application.Points;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FamilyJobsBoard.Api.Features.PointRedemptions;

internal static class PointRedemptionEndpoints
{
    public const string InsufficientPointsCode = "insufficientPoints";
    public const string RequestConflictCode = "requestConflict";

    public static IEndpointRouteBuilder MapPointRedemptionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api")
            .WithTags("Point redemptions")
            .RequireAuthorization("Adult");

        group.MapPost("/point-redemptions", RedeemAsync)
            .WithName("RedeemPoints")
            .WithSummary("Spend a child's points on a reward.")
            .WithDescription(
                "Adult-only. Records a redemption of a positive number of points with a required reward as its own append-only, negative ledger entry. A redemption can never take the balance below zero: asking for more points than the child has returns 409 with code 'insufficientPoints' and the current balance. Repeating a request ID with the same details returns the original result without a second entry; reusing it for different details returns 409 with code 'requestConflict'.");

        return endpoints;
    }

    private static async Task<Results<
        Created<RedeemPointsResponse>,
        Ok<RedeemPointsResponse>,
        ValidationProblem,
        Conflict<ProblemDetails>>> RedeemAsync(
        RedeemPointsRequest request,
        HttpContext context,
        PointRedemptionService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.RedeemAsync(
                new RedeemPoints(
                    request.RequestId,
                    IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                    request.ChildId,
                    request.Points,
                    request.Reward),
                cancellationToken);
            var redemption = result.Redemption;
            var response = new RedeemPointsResponse(
                new PointRedemptionResponse(
                    redemption.Id,
                    redemption.ChildId,
                    redemption.RedeemedByMemberId,
                    redemption.Points,
                    redemption.Reward,
                    redemption.RedeemedAtUtc),
                result.PointsBalance);
            return result.WasCreated
                ? TypedResults.Created($"/api/point-redemptions/{request.RequestId}", response)
                : TypedResults.Ok(response);
        }
        catch (InvalidPointRedemptionException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors,
                title: "Invalid point redemption data");
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
        catch (PointRedemptionRequestConflictException exception)
        {
            var problem = new ProblemDetails
            {
                Title = "Point redemption request conflict",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict,
            };
            problem.Extensions["code"] = RequestConflictCode;
            return TypedResults.Conflict(problem);
        }
    }
}
