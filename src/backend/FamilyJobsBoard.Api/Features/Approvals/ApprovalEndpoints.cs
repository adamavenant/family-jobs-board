using FamilyJobsBoard.Api.Features.Today;
using FamilyJobsBoard.Application.Approvals;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FamilyJobsBoard.Api.Features.Approvals;

internal static class ApprovalEndpoints
{
    public static IEndpointRouteBuilder MapApprovalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").WithTags("Approvals").RequireAuthorization("Adult");

        group.MapGet("/approvals/pending", GetPendingApprovalsAsync)
            .WithName("GetPendingApprovals")
            .WithSummary("List every job awaiting an adult's review.")
            .WithDescription(
                "Adult-only. Returns pending-approval jobs for every active child across all scheduled dates, oldest scheduled date first and in agenda order within a date. Decisions use the existing approve and reject operations.");

        return endpoints;
    }

    private static async Task<Ok<PendingApprovalsResponse>> GetPendingApprovalsAsync(
        PendingApprovalsService service,
        CancellationToken cancellationToken)
    {
        var approvals = await service.GetAsync(cancellationToken);
        return TypedResults.Ok(new PendingApprovalsResponse(
            approvals.Jobs.Select(TodayResponseMapping.MapJob).ToArray()));
    }
}
