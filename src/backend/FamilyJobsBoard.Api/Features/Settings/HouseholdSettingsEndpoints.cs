using FamilyJobsBoard.Api.Features.Identity;
using FamilyJobsBoard.Application.Settings;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FamilyJobsBoard.Api.Features.Settings;

internal static class HouseholdSettingsEndpoints
{
    public static IEndpointRouteBuilder MapHouseholdSettingsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/household-settings")
            .WithTags("Household settings")
            .RequireAuthorization();

        group.MapGet("", GetAsync)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName("GetHouseholdSettings")
            .WithSummary("Get the household's shared settings.")
            .WithDescription(
                "Any signed-in family member may read the settings every device in the household uses. undoGracePeriodSeconds is how long an action waits with an Undo toast before it is saved; a household that has never changed it uses 30 seconds.");

        group.MapPut("", UpdateAsync)
            .RequireAuthorization("Adult")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("UpdateHouseholdSettings")
            .WithSummary("Change the household's shared settings.")
            .WithDescription(
                "Adult-only; children receive 403. undoGracePeriodSeconds must be whole seconds from 5 to 120, otherwise 400 with validation errors. The change applies to later actions on every device.");

        return endpoints;
    }

    private static async Task<Ok<HouseholdSettingsResponse>> GetAsync(
        HouseholdSettingsService service,
        CancellationToken cancellationToken)
    {
        return TypedResults.Ok(Map(await service.GetAsync(cancellationToken)));
    }

    private static async Task<Results<Ok<HouseholdSettingsResponse>, ValidationProblem>> UpdateAsync(
        UpdateHouseholdSettingsRequest request,
        HttpContext context,
        HouseholdSettingsService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = await service.UpdateAsync(
                IdentityEndpoints.PrincipalMemberId(context.User)!.Value,
                new UpdateHouseholdSettings(request.UndoGracePeriodSeconds),
                cancellationToken);
            return TypedResults.Ok(Map(settings));
        }
        catch (InvalidHouseholdSettingsException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors,
                title: "Invalid household settings");
        }
    }

    private static HouseholdSettingsResponse Map(HouseholdSettingsView settings) =>
        new(
            settings.UndoGracePeriodSeconds,
            settings.MinimumUndoGracePeriodSeconds,
            settings.MaximumUndoGracePeriodSeconds);
}
