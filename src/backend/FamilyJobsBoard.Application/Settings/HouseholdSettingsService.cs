using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Domain.Settings;

namespace FamilyJobsBoard.Application.Settings;

public sealed class HouseholdSettingsService
{
    private readonly IHouseholdSettingsRepository _repository;
    private readonly IHouseholdClock _clock;

    public HouseholdSettingsService(IHouseholdSettingsRepository repository, IHouseholdClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<HouseholdSettingsView> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await _repository.GetAsync(cancellationToken)
            ?? HouseholdSettings.CreateDefault();
        return Map(settings);
    }

    public async Task<HouseholdSettingsView> UpdateAsync(
        Guid actorMemberId,
        UpdateHouseholdSettings request,
        CancellationToken cancellationToken)
    {
        if (request.UndoGracePeriodSeconds is not { } seconds
            || !HouseholdSettings.IsValidUndoGracePeriod(seconds))
        {
            throw new InvalidHouseholdSettingsException(new Dictionary<string, string[]>
            {
                [nameof(UpdateHouseholdSettings.UndoGracePeriodSeconds)] =
                [
                    $"Choose whole seconds from {HouseholdSettings.MinimumUndoGracePeriodSeconds} to {HouseholdSettings.MaximumUndoGracePeriodSeconds}.",
                ],
            });
        }

        try
        {
            return await SaveAsync(actorMemberId, seconds, cancellationToken);
        }
        catch (HouseholdSettingsCreatedConcurrentlyException)
        {
            // The first save for the household raced another; the row exists now, so update it.
            return await SaveAsync(actorMemberId, seconds, cancellationToken);
        }
    }

    private async Task<HouseholdSettingsView> SaveAsync(
        Guid actorMemberId,
        int undoGracePeriodSeconds,
        CancellationToken cancellationToken)
    {
        var settings = await _repository.GetForUpdateAsync(cancellationToken);
        if (settings is null)
        {
            settings = HouseholdSettings.CreateDefault();
            await _repository.AddAsync(settings, cancellationToken);
        }

        settings.ChangeUndoGracePeriod(undoGracePeriodSeconds, actorMemberId, _clock.UtcNow);
        await _repository.SaveChangesAsync(cancellationToken);
        return Map(settings);
    }

    private static HouseholdSettingsView Map(HouseholdSettings settings) =>
        new(
            settings.UndoGracePeriodSeconds,
            HouseholdSettings.MinimumUndoGracePeriodSeconds,
            HouseholdSettings.MaximumUndoGracePeriodSeconds);
}
