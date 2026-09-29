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
            .WithName("CompleteJob")
            .WithSummary("Complete an open job.")
            .WithDescription(
                "A child may submit their own job for approval. An adult must supply expectedPoints and, when it still matches, completes, approves, and awards atomically. A missing adult confirmation returns 400. Returns 404 when the job or its active child is unavailable, and 409 when the job is not open, changed after confirmation, changed concurrently, or its points were already awarded.");

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
            .WithDescription(
                "The adult supplies expectedPoints from the pending job shown to them. Returns 404 when the job or its active child is unavailable, and 409 unless the job is pending approval, when its points or state changed, or when its points were already awarded.");

        group.MapPost("/jobs/{id:guid}/reject", RejectJobAsync)
            .RequireAuthorization("Adult")
            .WithName("RejectJob")
            .WithSummary("Reject a pending job and return it for another try.")
            .WithDescription(
                "Records optional feedback. Returns 404 when the job or its active child is unavailable, and 409 unless the job is pending approval or when it changed concurrently.");

        group.MapPut("/jobs/{id:guid}", UpdateJobAsync)
            .RequireAuthorization("Adult")
            .WithName("UpdateJob")
            .WithSummary("Edit an open or pending-approval job occurrence.")
            .WithDescription(
                "Updates only this job occurrence without changing its recurring series or points ledger. Returns 404 when the job or its active child is unavailable, and 409 when it is approved, cancelled, or changed concurrently.");

        group.MapPost("/jobs/{id:guid}/cancel", CancelJobAsync)
            .RequireAuthorization("Adult")
            .WithName("CancelJob")
            .WithSummary("Cancel an open or pending-approval job occurrence.")
            .WithDescription(
                "Records an optional reason, hides the occurrence from daily agendas, and leaves its recurring series and review history unchanged. Returns 404 when the job or its active child is unavailable, and 409 when the job cannot be cancelled or changed concurrently.");

        group.MapPost("/jobs/{id:guid}/recurring-change/preview", PreviewRecurringJobChangeAsync)
            .RequireAuthorization("Adult")
            .WithName("PreviewRecurringJobChange")
            .WithSummary("Preview a This Only, All Future, or All recurring-job change.")
            .WithDescription(
                "Calculates updated, created, cancelled, and immutable occurrence counts without changing data.");

        group.MapPost("/jobs/{id:guid}/recurring-change", ApplyRecurringJobChangeAsync)
            .RequireAuthorization("Adult")
            .WithName("ApplyRecurringJobChange")
            .WithSummary("Atomically apply a scoped recurring-job edit or cancellation.")
            .WithDescription(
                "Recalculates the impact, preserves approved/cancelled history and ledger entries, and is request-ID idempotent.");

        return endpoints;
    }

    private static async Task<Results<
        Ok<RecurringJobChangePreviewResponse>,
        ValidationProblem,
        NotFound<ProblemDetails>,
        Conflict<ProblemDetails>,
        ForbidHttpResult>> PreviewRecurringJobChangeAsync(
        Guid id,
        RecurringJobChangeRequest request,
        HttpContext context,
        RecurringJobChangeService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var preview = await service.PreviewAsync(
                id,
                IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                MapChange(request),
                cancellationToken);
            return TypedResults.Ok(new RecurringJobChangePreviewResponse(
                preview.SeriesVersion,
                MapScopePreview(preview.ThisOnly),
                MapScopePreview(preview.AllFuture),
                MapScopePreview(preview.All)));
        }
        catch (InvalidRecurringJobChangeException exception)
        {
            return TypedResults.ValidationProblem(exception.Errors, title: "Invalid recurring job change");
        }
        catch (JobNotFoundException exception)
        {
            return TypedResults.NotFound(NotFoundProblem(exception.Message));
        }
        catch (RecurringJobSeriesNotFoundException)
        {
            return TypedResults.NotFound(NotFoundProblem("Recurring job series not found."));
        }
        catch (RecurringJobChangeConflictException exception)
        {
            return TypedResults.Conflict(ConflictProblem(exception.Message));
        }
        catch (RecurringJobChangeForbiddenException)
        {
            return TypedResults.Forbid();
        }
    }

    private static async Task<Results<
        Ok<RecurringJobChangeResultResponse>,
        ValidationProblem,
        NotFound<ProblemDetails>,
        Conflict<ProblemDetails>,
        ForbidHttpResult>> ApplyRecurringJobChangeAsync(
        Guid id,
        ApplyRecurringJobChangeRequest request,
        HttpContext context,
        RecurringJobChangeService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.ApplyAsync(
                id,
                IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                new ApplyRecurringJobChange(request.RequestId, MapChange(request.Change)),
                cancellationToken);
            return TypedResults.Ok(new RecurringJobChangeResultResponse(
                result.RequestId,
                result.SeriesId,
                result.SeriesVersion,
                result.Operation,
                result.Scope,
                MapImpact(result.Impact)));
        }
        catch (InvalidRecurringJobChangeException exception)
        {
            return TypedResults.ValidationProblem(exception.Errors, title: "Invalid recurring job change");
        }
        catch (JobNotFoundException exception)
        {
            return TypedResults.NotFound(NotFoundProblem(exception.Message));
        }
        catch (RecurringJobSeriesNotFoundException)
        {
            return TypedResults.NotFound(NotFoundProblem("Recurring job series not found."));
        }
        catch (RecurringJobChangeConflictException exception)
        {
            return TypedResults.Conflict(ConflictProblem(exception.Message));
        }
        catch (RecurringJobChangeForbiddenException)
        {
            return TypedResults.Forbid();
        }
    }

    private static RecurringJobChangeInput MapChange(RecurringJobChangeRequest request) => new(
        request.Operation,
        request.Scope,
        request.Reason,
        request.ExpectedSeriesVersion,
        request.Name,
        request.Description,
        request.Points,
        request.ScheduledDate,
        request.AgendaPeriod,
        request.ScheduledTime,
        request.Frequency,
        request.Weekdays,
        request.DayOfMonth,
        request.EndDate);

    private static RecurringJobChangeImpactResponse MapImpact(RecurringJobChangeImpact impact) => new(
        impact.UpdatedCount,
        impact.CreatedCount,
        impact.CancelledCount,
        impact.ApprovedSkippedCount,
        impact.CancelledSkippedCount,
        impact.RetrospectivePointIncreaseSkippedCount,
        impact.Warnings);

    private static RecurringJobScopePreviewResponse MapScopePreview(
        RecurringJobScopePreview preview) => new(
            preview.Impact is null ? null : MapImpact(preview.Impact),
            preview.Error);

    private static ProblemDetails NotFoundProblem(string detail) => new()
    {
        Title = "Recurring job not found",
        Detail = detail,
        Status = StatusCodes.Status404NotFound,
    };

    private static ProblemDetails ConflictProblem(string detail) => new()
    {
        Title = "Recurring job changed",
        Detail = detail,
        Status = StatusCodes.Status409Conflict,
    };

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
        catch (Exception exception) when (
            exception is JobEditRejectedException or JobStateConflictException)
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
        catch (Exception exception) when (
            exception is JobCancellationRejectedException or JobStateConflictException)
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

    private static async Task<Results<Ok<JobResponse>, ValidationProblem, NotFound<ProblemDetails>, Conflict<ProblemDetails>, ForbidHttpResult>>
        CompleteJobAsync(
            Guid id,
            [FromQuery] int? expectedPoints,
            HttpContext context,
            TodayBoardService service,
            CancellationToken cancellationToken)
    {
        try
        {
            var memberId = IdentityEndpoints.PrincipalMemberId(context.User)!.Value;
            var job = await service.CompleteAsync(
                id,
                memberId,
                expectedPoints,
                cancellationToken);
            return TypedResults.Ok(MapJob(job));
        }
        catch (InvalidJobCompletionException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors,
                title: "Invalid job completion");
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
            exception is JobCompletionRejectedException
                or JobPointsConfirmationConflictException
                or DuplicateJobPointsAwardException
                or JobStateConflictException)
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
            [FromQuery] int expectedPoints,
            TodayBoardService service,
            CancellationToken cancellationToken)
    {
        try
        {
            var approval = await service.ApproveAsync(id, expectedPoints, cancellationToken);
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
            exception is JobApprovalRejectedException
                or JobPointsConfirmationConflictException
                or DuplicateJobPointsAwardException
                or JobStateConflictException)
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
        catch (Exception exception) when (
            exception is JobRejectionRejectedException or JobStateConflictException)
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
            board.PendingApprovalCount,
            board.WhoseTurns
                .Select(turn => new WhoseTurnResponse(
                    turn.RotationId, turn.Question, turn.ChildId, turn.ChildDisplayName))
                .ToArray());
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

    private static JobResponse MapJob(TodayJob job) => TodayResponseMapping.MapJob(job);
}
