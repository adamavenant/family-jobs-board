using FamilyJobsBoard.Application.Today;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Domain.Points;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FamilyJobsBoard.Api.Features.Today;

internal static class TodayEndpoints
{
    public static IEndpointRouteBuilder MapTodayEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").WithTags("Today");

        group.MapGet("/today", GetTodayAsync)
            .WithName("GetToday")
            .WithSummary("Get the selected demo family member's board.")
            .WithDescription("Defaults to Fredster and returns a role-appropriate view for the selected household member.");

        group.MapPost("/jobs/{id:guid}/complete", CompleteJobAsync)
            .WithName("CompleteJob")
            .WithSummary("Mark an open job complete and pending approval.")
            .WithDescription("Returns 409 when the job has already been completed.");

        group.MapPost("/today/jobs", AddJobAsync)
            .WithName("AddJob")
            .WithSummary("Add a new job for a child.")
            .WithDescription("Adds a new job to the selected child's board for today.");

        group.MapPost("/jobs/{id:guid}/approve", ApproveJobAsync)
            .WithName("ApproveJob")
            .WithSummary("Approve a pending job and award its points.")
            .WithDescription("Returns 409 unless the job is pending approval or its points were already awarded.");

        group.MapPost("/jobs/{id:guid}/reject", RejectJobAsync)
            .WithName("RejectJob")
            .WithSummary("Reject a pending job and return it for another try.")
            .WithDescription("Records optional feedback and returns 409 unless the job is pending approval.");

        return endpoints;
    }

    private static async Task<Results<Ok<TodayResponse>, ProblemHttpResult>> GetTodayAsync(
        Guid? memberId,
        TodayBoardService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var board = await service.GetAsync(
                DemoDataIds.Fredster,
                memberId,
                cancellationToken);
            return TypedResults.Ok(MapBoard(board));
        }
        catch (HouseholdMemberNotFoundException exception)
        {
            return TypedResults.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Household member not found");
        }
        catch (TodayBoardNotAvailableException exception)
        {
            return TypedResults.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Today board unavailable");
        }
    }

    private static async Task<Results<Created<JobResponse>, ValidationProblem, ProblemHttpResult>> AddJobAsync(
        AddJobRequest request,
        TodayBoardService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var job = await service.AddJobAsync(
                new AddTodayJob(
                    request.ChildId,
                    request.Name,
                    request.Description,
                    request.Points),
                cancellationToken);
            return TypedResults.Created("/api/today", MapJob(job));
        }
        catch (InvalidTodayJobException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors,
                title: "Invalid job data");
        }
        catch (TodayBoardNotAvailableException exception)
        {
            return TypedResults.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Today board unavailable");
        }
    }

    private static async Task<Results<Ok<JobResponse>, NotFound<ProblemDetails>, Conflict<ProblemDetails>>>
        CompleteJobAsync(
            Guid id,
            TodayBoardService service,
            CancellationToken cancellationToken)
    {
        try
        {
            var job = await service.CompleteAsync(id, cancellationToken);
            return TypedResults.Ok(MapJob(job));
        }
        catch (JobNotFoundException exception)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Job not found",
                Detail = exception.Message,
                Status = StatusCodes.Status404NotFound,
            });
        }
        catch (JobCompletionRejectedException exception)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Job cannot be completed",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict,
            });
        }
    }

    private static async Task<Results<Ok<JobApprovalResponse>, NotFound<ProblemDetails>, Conflict<ProblemDetails>>>
        ApproveJobAsync(
            Guid id,
            TodayBoardService service,
            CancellationToken cancellationToken)
    {
        try
        {
            var approval = await service.ApproveAsync(id, cancellationToken);
            return TypedResults.Ok(new JobApprovalResponse(
                MapJob(approval.Job),
                approval.PointsBalance));
        }
        catch (JobNotFoundException exception)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Job not found",
                Detail = exception.Message,
                Status = StatusCodes.Status404NotFound,
            });
        }
        catch (Exception exception) when (
            exception is JobApprovalRejectedException or DuplicateJobPointsAwardException)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Job cannot be approved",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict,
            });
        }
    }

    private static async Task<Results<Ok<JobResponse>, ValidationProblem, NotFound<ProblemDetails>, Conflict<ProblemDetails>>>
        RejectJobAsync(
            Guid id,
            RejectJobRequest request,
            TodayBoardService service,
            CancellationToken cancellationToken)
    {
        try
        {
            var job = await service.RejectAsync(id, request.Reason, cancellationToken);
            return TypedResults.Ok(MapJob(job));
        }
        catch (InvalidJobRejectionException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors,
                title: "Invalid rejection data");
        }
        catch (JobNotFoundException exception)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Job not found",
                Detail = exception.Message,
                Status = StatusCodes.Status404NotFound,
            });
        }
        catch (JobRejectionRejectedException exception)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Job cannot be rejected",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict,
            });
        }
    }

    private static TodayResponse MapBoard(TodayBoard board)
    {
        return new TodayResponse(
            MapMember(board.Viewer),
            board.Members.Select(MapMember).ToArray(),
            board.Date,
            board.Jobs.Select(MapJob).ToArray(),
            board.PointsBalance,
            board.PointEarnings.Select(MapPointEarning).ToArray(),
            board.PendingApprovalCount);
    }

    private static MemberResponse MapMember(TodayMember member)
    {
        return new MemberResponse(
            member.Id,
            member.FirstName,
            member.Nickname,
            member.DisplayName,
            member.IsAdult);
    }

    private static PointEarningResponse MapPointEarning(TodayPointEarning earning)
    {
        return new PointEarningResponse(
            earning.Id,
            earning.JobId,
            earning.JobName,
            earning.Points,
            earning.AwardedAtUtc);
    }

    private static JobResponse MapJob(TodayJob job)
    {
        return new JobResponse(
            job.Id,
            job.ChildId,
            job.ChildDisplayName,
            job.Name,
            job.Description,
            job.Points,
            job.Status,
            job.CompletedAtUtc,
            job.ApprovedAtUtc,
            job.LatestRejection is null
                ? null
                : new JobRejectionResponse(
                    job.LatestRejection.DecisionId,
                    job.LatestRejection.Reason,
                    job.LatestRejection.RejectedAtUtc));
    }
}
