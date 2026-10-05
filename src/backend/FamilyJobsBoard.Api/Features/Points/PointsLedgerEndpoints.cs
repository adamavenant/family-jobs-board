using FamilyJobsBoard.Api.Features.Identity;
using FamilyJobsBoard.Application.Points;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FamilyJobsBoard.Api.Features.Points;

internal static class PointsLedgerEndpoints
{
    public static IEndpointRouteBuilder MapPointsLedgerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").WithTags("Points ledger").RequireAuthorization();

        group.MapGet("/points-ledger", GetLedgerAsync)
            .WithName("GetPointsLedger")
            .Produces(StatusCodes.Status403Forbidden)
            .WithSummary("Get the points ledger, newest first.")
            .WithDescription(
                $"Children always receive their own ledger; any other childId returns 403. Adults receive every child's ledger, or one child's with childId; an unknown or adult ID returns 400. Each page holds up to {PointsLedgerService.PageSize} entries with the name, signed points, award time, the child's balance after the entry, and recordedByDisplayName: the adult who logged the behaviour, made the adjustment, or redeemed the points (null for job awards). Pass nextCursor as 'before' to read older entries.");

        return endpoints;
    }

    private static async Task<Results<Ok<PointsLedgerResponse>, ValidationProblem, ForbidHttpResult>>
        GetLedgerAsync(
            [FromQuery] string? childId,
            [FromQuery] string? before,
            HttpContext context,
            PointsLedgerService service,
            CancellationToken cancellationToken)
    {
        // Parsed here rather than bound as Guid? so a malformed ID gets the same
        // validation problem as an unknown one.
        Guid? selectedChildId = null;
        if (!string.IsNullOrEmpty(childId))
        {
            if (!Guid.TryParse(childId, out var parsed))
            {
                return TypedResults.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["ChildId"] = ["Choose a child in this household."],
                    },
                    title: "Invalid points ledger request");
            }

            selectedChildId = parsed;
        }

        try
        {
            var ledger = await service.GetAsync(
                IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                selectedChildId,
                string.IsNullOrEmpty(before) ? null : before,
                cancellationToken);
            return TypedResults.Ok(Map(ledger));
        }
        catch (InvalidPointsLedgerRequestException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors,
                title: "Invalid points ledger request");
        }
        catch (PointsLedgerForbiddenException)
        {
            return TypedResults.Forbid();
        }
    }

    private static PointsLedgerResponse Map(PointsLedger ledger)
    {
        return new PointsLedgerResponse(
            ledger.SelectedChildId,
            ledger.Children
                .Select(child => new PointsLedgerChildResponse(
                    child.Id,
                    child.DisplayName,
                    child.IsActive,
                    child.Balance))
                .ToArray(),
            ledger.Entries
                .Select(entry => new PointsLedgerEntryResponse(
                    entry.Id,
                    entry.ChildId,
                    entry.ChildDisplayName,
                    entry.Name,
                    entry.Points,
                    entry.BalanceAfter,
                    entry.AwardedAtUtc,
                    entry.RecordedByDisplayName))
                .ToArray(),
            ledger.NextCursor);
    }
}
