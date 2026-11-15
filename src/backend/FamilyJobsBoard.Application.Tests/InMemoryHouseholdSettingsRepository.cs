using FamilyJobsBoard.Application.Settings;
using FamilyJobsBoard.Domain.Settings;

namespace FamilyJobsBoard.Application.Tests;

/// <summary>
/// Holds the household's settings in memory. <see cref="ConcurrentInsert"/> simulates another
/// request saving the first settings between this request's read and its insert.
/// </summary>
internal sealed class InMemoryHouseholdSettingsRepository : IHouseholdSettingsRepository
{
    private HouseholdSettings? _pending;

    public HouseholdSettings? Saved { get; private set; }

    public HouseholdSettings? ConcurrentInsert { get; set; }

    public int SaveCount { get; private set; }

    public Task<HouseholdSettings?> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Saved);

    public Task<HouseholdSettings?> GetForUpdateAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Saved);

    public Task AddAsync(HouseholdSettings settings, CancellationToken cancellationToken)
    {
        _pending = settings;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (_pending is not null && ConcurrentInsert is not null)
        {
            Saved = ConcurrentInsert;
            ConcurrentInsert = null;
            _pending = null;
            throw new HouseholdSettingsCreatedConcurrentlyException();
        }

        Saved ??= _pending;
        _pending = null;
        SaveCount++;
        return Task.CompletedTask;
    }
}
