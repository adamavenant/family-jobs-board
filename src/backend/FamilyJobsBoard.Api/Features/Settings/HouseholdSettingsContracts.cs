namespace FamilyJobsBoard.Api.Features.Settings;

public sealed record HouseholdSettingsResponse(
    int UndoGracePeriodSeconds,
    int MinimumUndoGracePeriodSeconds,
    int MaximumUndoGracePeriodSeconds);

public sealed record UpdateHouseholdSettingsRequest(int? UndoGracePeriodSeconds);
