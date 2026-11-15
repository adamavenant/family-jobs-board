namespace FamilyJobsBoard.Application.Settings;

public sealed record HouseholdSettingsView(
    int UndoGracePeriodSeconds,
    int MinimumUndoGracePeriodSeconds,
    int MaximumUndoGracePeriodSeconds);

public sealed record UpdateHouseholdSettings(int? UndoGracePeriodSeconds);
