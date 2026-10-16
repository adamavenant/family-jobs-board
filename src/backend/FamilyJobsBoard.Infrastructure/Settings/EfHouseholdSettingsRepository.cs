using FamilyJobsBoard.Application.Settings;
using FamilyJobsBoard.Domain.Settings;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FamilyJobsBoard.Infrastructure.Settings;

public sealed class EfHouseholdSettingsRepository : IHouseholdSettingsRepository
{
    private readonly AppDbContext _database;

    public EfHouseholdSettingsRepository(AppDbContext database)
    {
        _database = database;
    }

    public Task<HouseholdSettings?> GetAsync(CancellationToken cancellationToken)
    {
        return _database.HouseholdSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                settings => settings.Id == HouseholdSettings.SingletonId,
                cancellationToken);
    }

    public Task<HouseholdSettings?> GetForUpdateAsync(CancellationToken cancellationToken)
    {
        return _database.HouseholdSettings.SingleOrDefaultAsync(
            settings => settings.Id == HouseholdSettings.SingletonId,
            cancellationToken);
    }

    public async Task AddAsync(HouseholdSettings settings, CancellationToken cancellationToken)
    {
        await _database.HouseholdSettings.AddAsync(settings, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: HouseholdSettingsConfiguration.PrimaryKeyName,
            })
        {
            // Detach the failed insert so the caller can update the row that won.
            _database.ChangeTracker.Clear();
            throw new HouseholdSettingsCreatedConcurrentlyException();
        }
    }
}
