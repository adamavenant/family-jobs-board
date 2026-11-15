using FamilyJobsBoard.Domain.Settings;

namespace FamilyJobsBoard.Application.Settings;

public interface IHouseholdSettingsRepository
{
    /// <summary>Returns the saved settings, or null when the household has never saved any.</summary>
    Task<HouseholdSettings?> GetAsync(CancellationToken cancellationToken);

    /// <summary>Returns the saved settings tracked for an update, or null when none are saved.</summary>
    Task<HouseholdSettings?> GetForUpdateAsync(CancellationToken cancellationToken);

    Task AddAsync(HouseholdSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Persists pending changes. Throws <see cref="HouseholdSettingsCreatedConcurrentlyException"/>
    /// when a concurrent request inserted the settings first.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
