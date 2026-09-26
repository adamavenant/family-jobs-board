using FamilyJobsBoard.Application.Today;
using FamilyJobsBoard.Api.Features.Identity;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Domain.Points;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FamilyJobsBoard.Api.Features.Today;

internal static class TodayEndpoints
{
    public static IEndpointRouteBuilder MapTodayEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").WithTags("Today").RequireAuthorization();

        group.MapGet("/today", GetTodayAsync)
            .WithName("GetToday")
            .WithSummary("Get the authenticated family member's daily board.")
            .WithDescription(
                "Returns the child-owned or adult household view for the optional household-local date and adult-only child filter.");

        group.MapPost("/jobs/{id:guid}/complete", CompleteJobAsync)
            .RequireAuthorization("Child")
            .WithName("CompleteJob")
            .WithSummary("Mark an open job complete and pending approval.")
            .WithDescription("Returns 409 when the job has already been completed.");

        group.MapPost("/today/jobs", AddJobAsync)
            .RequireAuthorization("Adult")
            .WithName("AddJob")
            .WithSummary("Schedule a once-off job for one or more children.")
            .WithDescription(
                "Adds an independent job to each selected child's board for a household-local date.");

        group.MapPost("/recurring-jobs/daily", CreateDailyRecurringJobAsync)
            .RequireAuthorization("Adult")
            .WithName("CreateDailyRecurringJob")
            .WithSummary("Create a daily recurring job for one or more children.")
            .WithDescription(
                "Creates an adult-owned child-specific daily series for each assignee and materializes duplicate-safe occurrences through an eight-week horizon. With assignmentMode takeTurns, one series rotates occurrences round robin through childIds in the order given.");

        group.MapPost("/recurring-jobs/weekly", CreateWeeklyRecurringJobAsync)
            .RequireAuthorization("Adult")
            .WithName("CreateWeeklyRecurringJob")
            .WithSummary("Create a weekly recurring job for one or more children.")
            .WithDescription(
                "Creates an adult-owned child-specific weekly series for each assignee and materializes duplicate-safe occurrences through an eight-week horizon. With assignmentMode takeTurns, one series rotates occurrences round robin through childIds in the order given.");

        group.MapPost("/recurring-jobs/monthly", CreateMonthlyRecurringJobAsync)
            .RequireAuthorization("Adult")
            .WithName("CreateMonthlyRecurringJob")
            .WithSummary("Create a monthly recurring job for one or more children.")
            .WithDescription(
                "Creates an adult-owned child-specific monthly series for each assignee and uses the final valid day in shorter months. With assignmentMode takeTurns, one series rotates occurrences round robin through childIds in the order given.");

        group.MapPost("/jobs/{id:guid}/approve", ApproveJobAsync)
            .RequireAuthorization("Adult")
            .WithName("ApproveJob")
            .WithSummary("Approve a pending job and award its points.")
            .WithDescription("Returns 409 unless the job is pending approval or its points were already awarded.");

        group.MapPost("/jobs/{id:guid}/reject", RejectJobAsync)
            .RequireAuthorization("Adult")
            .WithName("RejectJob")
            .WithSummary("Reject a pending job and return it for another try.")
            .WithDescription("Records optional feedback and returns 409 unless the job is pending approval.");

        group.MapPut("/jobs/{id:guid}", UpdateJobAsync)
            .RequireAuthorization("Adult")
            .WithName("UpdateJob")
            .WithSummary("Edit an open or pending-approval job occurrence.")
            .WithDescription(
                "Updates only this job occurrence without changing its recurring series or points ledger; approved and cancelled jobs return 409.");

        group.MapPost("/jobs/{id:guid}/cancel", CancelJobAsync)
            .RequireAuthorization("Adult")
            .WithName("CancelJob")
            .WithSummary("Cancel an open or pending-approval job occurrence.")
            .WithDescription(
                "Records an optional reason, hides the occurrence from daily agendas, and leaves its recurring series and review history unchanged.");

        return endpoints;
    }

    private static async Task<Results<
        Ok<JobResponse>,
        ValidationProblem,
        NotFound<ProblemDetails>,
        Conflict<ProblemDetails>,
        ForbidHttpResult>> UpdateJobAsync(
        Guid id,
        UpdateJobRequest request,
        HttpContext context,
        TodayBoardService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var job = await service.UpdateJobAsync(
                id,
                new UpdateTodayJob(
                    IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                    request.Name,
                    request.Description,
                    request.Points,
                    request.ScheduledDate,
                    request.AgendaPeriod,
                    request.ScheduledTime),
                cancellationToken);
            return TypedResults.Ok(MapJob(job));
        }
        catch (InvalidJobEditException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors,
                title: "Invalid job data");
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
        catch (JobEditRejectedException exception)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Job cannot be edited",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict,
            });
        }
        catch (JobManagementForbiddenException)
        {
            return TypedResults.Forbid();
        }
    }

    private static async Task<Results<
        Ok<JobResponse>,
        ValidationProblem,
        NotFound<ProblemDetails>,
        Conflict<ProblemDetails>,
        ForbidHttpResult>> CancelJobAsync(
        Guid id,
        CancelJobRequest request,
        HttpContext context,
        TodayBoardService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var job = await service.CancelJobAsync(
                id,
                IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                request.Reason,
                cancellationToken);
            return TypedResults.Ok(MapJob(job));
        }
        catch (InvalidJobCancellationException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors,
                title: "Invalid cancellation data");
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
        catch (JobCancellationRejectedException exception)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Job cannot be cancelled",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict,
            });
        }
        catch (JobManagementForbiddenException)
        {
            return TypedResults.Forbid();
        }
    }

    private static async Task<Results<
        Created<RecurringJobResponse>,
        Ok<RecurringJobResponse>,
        ValidationProblem,
        Conflict<ProblemDetails>>> CreateDailyRecurringJobAsync(
        CreateDailyRecurringJobRequest request,
        HttpContext context,
        TodayBoardService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var creation = await service.CreateDailyRecurringJobAsync(
                new CreateDailyRecurringJob(
                    request.RequestId,
                    IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                    request.ChildIds,
                    request.Name,
                    request.Description,
                    request.Points,
                    request.AgendaPeriod,
                    request.ScheduledTime,
                    request.StartDate,
                    request.EndDate,
                    request.AssignmentMode),
                cancellationToken);
            var response = MapRecurringCreation(creation);
            return creation.WasCreated
                ? TypedResults.Created($"/api/recurring-jobs/daily/{request.RequestId}", response)
                : TypedResults.Ok(response);
        }
        catch (InvalidDailyRecurringJobException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors,
                title: "Invalid daily recurring job data");
        }
        catch (DailyRecurringJobRequestConflictException exception)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Recurring job request conflict",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict,
            });
        }
    }

    private static async Task<Results<
        Created<RecurringJobResponse>,
        Ok<RecurringJobResponse>,
        ValidationProblem,
        Conflict<ProblemDetails>>> CreateMonthlyRecurringJobAsync(
        CreateMonthlyRecurringJobRequest request,
        HttpContext context,
        TodayBoardService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var creation = await service.CreateMonthlyRecurringJobAsync(
                new CreateMonthlyRecurringJob(
                    request.RequestId,
                    IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                    request.ChildIds,
                    request.Name,
                    request.Description,
                    request.Points,
                    request.AgendaPeriod,
                    request.ScheduledTime,
                    request.StartDate,
                    request.EndDate,
                    request.DayOfMonth,
                    request.AssignmentMode),
                cancellationToken);
            var response = MapRecurringCreation(creation);
            return creation.WasCreated
                ? TypedResults.Created($"/api/recurring-jobs/monthly/{request.RequestId}", response)
                : TypedResults.Ok(response);
        }
        catch (InvalidMonthlyRecurringJobException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors,
                title: "Invalid monthly recurring job data");
        }
        catch (MonthlyRecurringJobRequestConflictException exception)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Recurring job request conflict",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict,
            });
        }
    }

    private static async Task<Results<
        Created<RecurringJobResponse>,
        Ok<RecurringJobResponse>,
        ValidationProblem,
        Conflict<ProblemDetails>>> CreateWeeklyRecurringJobAsync(
        CreateWeeklyRecurringJobRequest request,
        HttpContext context,
        TodayBoardService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var creation = await service.CreateWeeklyRecurringJobAsync(
                new CreateWeeklyRecurringJob(
                    request.RequestId,
                    IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                    request.ChildIds,
                    request.Name,
                    request.Description,
                    request.Points,
                    request.AgendaPeriod,
                    request.ScheduledTime,
                    request.StartDate,
                    request.EndDate,
                    request.Weekdays,
                    request.AssignmentMode),
                cancellationToken);
            var response = MapRecurringCreation(creation);
            return creation.WasCreated
                ? TypedResults.Created($"/api/recurring-jobs/weekly/{request.RequestId}", response)
                : TypedResults.Ok(response);
        }
        catch (InvalidWeeklyRecurringJobException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors,
                title: "Invalid weekly recurring job data");
        }
        catch (WeeklyRecurringJobRequestConflictException exception)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Recurring job request conflict",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict,
            });
        }
    }

    private static async Task<Results<
        Ok<TodayResponse>,
        ValidationProblem,
        ForbidHttpResult,
        ProblemHttpResult>> GetTodayAsync(
        [FromQuery] DateOnly? date,
        [FromQuery] Guid? childId,
        HttpContext context,
        TodayBoardService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var memberId = IdentityEndpoints.PrincipalMemberId(context.User)!.Value;
            var board = await service.GetAsync(
                memberId,
                date ?? service.CurrentDate,
                childId,
                cancellationToken);
            return TypedResults.Ok(MapBoard(board));
        }
        catch (InvalidTodayBoardFilterException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors,
                title: "Invalid daily board filter");
        }
        catch (TodayBoardFilterForbiddenException)
        {
            return TypedResults.Forbid();
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

    private static async Task<Results<Created<AddJobsResponse>, ValidationProblem, ProblemHttpResult>> AddJobAsync(
        AddJobRequest request,
        TodayBoardService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var jobs = await service.AddJobAsync(
                new AddTodayJob(
                    request.ChildIds,
                    request.Name,
                    request.Description,
                    request.Points,
                    request.ScheduledDate,
                    request.AgendaPeriod,
                    request.ScheduledTime),
                cancellationToken);
            return TypedResults.Created(
                "/api/today",
                new AddJobsResponse(jobs.Select(MapJob).ToArray()));
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

    private static async Task<Results<Ok<JobResponse>, NotFound<ProblemDetails>, Conflict<ProblemDetails>, ForbidHttpResult>>
        CompleteJobAsync(
            Guid id,
            HttpContext context,
            TodayBoardService service,
            CancellationToken cancellationToken)
    {
        try
        {
            var memberId = IdentityEndpoints.PrincipalMemberId(context.User)!.Value;
            var job = await service.CompleteAsync(id, memberId, cancellationToken);
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
        catch (JobOwnershipRejectedException)
        {
            return TypedResults.Forbid();
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
            board.CurrentDate,
            board.SelectedChildId,
            board.Jobs.Select(MapJob).ToArray(),
            board.PointsBalance,
            board.PointEarnings.Select(MapPointEarning).ToArray(),
            board.PendingApprovalCount,
            board.WhoseTurn is null
                ? null
                : new WhoseTurnResponse(
                    board.WhoseTurn.Question,
                    board.WhoseTurn.ChildId,
                    board.WhoseTurn.ChildDisplayName));
    }

    private static RecurringJobResponse MapRecurringCreation(RecurringJobCreation creation)
    {
        return new RecurringJobResponse(
            creation.Assignments.Select(assignment => new RecurringJobAssignmentResponse(
                assignment.SeriesId,
                assignment.ChildId,
                assignment.GeneratedThrough,
                assignment.OccurrenceCount,
                assignment.RotationChildIds))
                .ToArray());
    }

    private static MemberResponse MapMember(TodayMember member) => TodayResponseMapping.MapMember(member);

    private static PointEarningResponse MapPointEarning(TodayPointEarning earning)
    {
        return new PointEarningResponse(
            earning.Id,
            earning.Source,
            earning.Name,
            earning.JobId,
            earning.Points,
            earning.AwardedAtUtc,
            earning.LoggedByDisplayName);
    }

    private static JobResponse MapJob(TodayJob job) => TodayResponseMapping.MapJob(job);
}
