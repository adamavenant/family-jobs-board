using FamilyJobsBoard.Application.Today;
using FamilyJobsBoard.Domain.Jobs;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using FamilyJobsBoard.Application.Today.Requests;

namespace FamilyJobsBoard.Api.Features.Today;

internal static class TodayEndpoints
{
    public static IEndpointRouteBuilder MapTodayEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").WithTags("Today");

        group.MapGet("/today", GetTodayAsync)
            .WithName("GetToday")
            .WithSummary("Get the demo child's jobs for the current household date.")
            .WithDescription("Returns the deterministic development child and jobs scheduled today.");

        group.MapPost("/jobs/{id:guid}/complete", CompleteJobAsync)
            .WithName("CompleteJob")
            .WithSummary("Mark an open job complete and pending approval.")
            .WithDescription("Returns 409 when the job has already been completed.");

        group.MapPost("/today/jobs", AddJobAsync)
            .WithName("AddJob")
            .WithSummary("Add a new job for the demo child.")
            .WithDescription("Adds a new job to the demo child's board for today.");

        return endpoints;
    }

    private static async Task<Results<Ok<TodayResponse>, ProblemHttpResult>> GetTodayAsync(
        TodayBoardService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var board = await service.GetAsync(cancellationToken);
            return TypedResults.Ok(MapBoard(board));
        }
        catch (TodayBoardNotAvailableException exception)
        {
            return TypedResults.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Today board unavailable");
        }
    }

    private static async Task<Results<Ok<JobResponse>, ProblemHttpResult>> AddJobAsync(
        AddJobRequest request,
        TodayBoardService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var job = await service.AddJobAsync(request.Name, request.Description, request.Points, cancellationToken);
            return TypedResults.Ok(MapJob(job));
        }
        catch (Exception exception) when (exception is not TodayBoardNotAvailableException)
        {
            return TypedResults.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid job data");
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

    private static TodayResponse MapBoard(TodayBoard board)
    {
        return new TodayResponse(
            new ChildResponse(board.ChildId, board.ChildName),
            board.Date,
            board.Jobs.Select(MapJob).ToArray());
    }

    private static JobResponse MapJob(TodayJob job)
    {
        return new JobResponse(
            job.Id,
            job.Name,
            job.Description,
            job.Points,
            job.Status,
            job.CompletedAtUtc);
    }
}
