namespace FamilyJobsBoard.Application.Settings;

public sealed class InvalidHouseholdSettingsException : Exception
{
    public InvalidHouseholdSettingsException(IReadOnlyDictionary<string, string[]> errors)
        : base("The household settings were invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>
/// Another request created the household's settings while this one was creating them too.
/// </summary>
public sealed class HouseholdSettingsCreatedConcurrentlyException : Exception
{
    public HouseholdSettingsCreatedConcurrentlyException()
        : base("The household settings were created by a concurrent request.")
    {
    }
}
