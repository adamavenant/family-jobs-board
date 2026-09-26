using FamilyJobsBoard.Api.Features.Identity;
using FamilyJobsBoard.Api.Features.Today;
using FamilyJobsBoard.Application.Calendar;
using FamilyJobsBoard.Application.Today;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FamilyJobsBoard.Api.Features.Calendar;

internal static class CalendarEndpoints
{
    public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").WithTags("Calendar").RequireAuthorization("Adult");

        group.MapGet("/calendar", GetCalendarAsync)
            .WithName("GetCalendar")
            .WithSummary("Get the adult day/week/month calendar.")
            .WithDescription(
                "Adult-only. Reads the same job occurrences and workflow state as the daily agenda for the requested household-local date range, so the two views never disagree. Defaults to the week containing today when view/date are omitted.");

        return endpoints;
    }

    private static async Task<Results<Ok<CalendarResponse>, ValidationProblem, NotFound<ProblemDetails>>>
        GetCalendarAsync(
            string? view,
            DateOnly? date,
            Guid? childId,
            HttpContext context,
            CalendarService service,
            CancellationToken cancellationToken)
    {
        try
        {
            var board = await service.GetAsync(
                IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                view ?? "week",
                date,
                childId,
                cancellationToken);
            return TypedResults.Ok(MapBoard(board));
        }
        catch (InvalidCalendarRequestException exception)
        {
            return TypedResults.ValidationProblem(exception.Errors, title: "Invalid calendar request");
        }
        catch (HouseholdMemberNotFoundException exception)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Household member not found",
                Detail = exception.Message,
                Status = StatusCodes.Status404NotFound,
            });
        }
        catch (TodayBoardNotAvailableException)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Household not available",
                Detail = "The household has not been set up yet.",
                Status = StatusCodes.Status404NotFound,
            });
        }
    }

    private static CalendarResponse MapBoard(CalendarBoard board)
    {
        return new CalendarResponse(
            TodayResponseMapping.MapMember(board.Viewer),
            board.Members.Select(TodayResponseMapping.MapMember).ToArray(),
            board.View switch
            {
                CalendarView.Day => "day",
                CalendarView.Week => "week",
                CalendarView.Month => "month",
                _ => throw new InvalidOperationException($"Unknown calendar view '{board.View}'."),
            },
            board.AnchorDate,
            board.CurrentDate,
            board.RangeStart,
            board.RangeEnd,
            board.SelectedChildId,
            board.Days
                .Select(day => new CalendarDayResponse(
                    day.Date,
                    day.IsInFocusedPeriod,
                    day.Jobs.Select(TodayResponseMapping.MapJob).ToArray()))
                .ToArray());
    }
}
