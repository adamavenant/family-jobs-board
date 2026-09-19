using FamilyJobsBoard.Application.Administration;
using FamilyJobsBoard.Domain.Administration;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyJobsBoard.Infrastructure.Administration;

public sealed class EfAdministrationRepository : IAdministrationRepository
{
    private readonly AppDbContext _database;

    public EfAdministrationRepository(AppDbContext database)
    {
        _database = database;
    }

    public async Task<AdminDataResetResult> ResetJobsAndPointsAsync(
        Guid initiatedByAdultId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _database.Database.BeginTransactionAsync(
            cancellationToken);

        var deletedPointsEntryCount = await _database.PointsLedgerEntries
            .ExecuteDeleteAsync(cancellationToken);
        var deletedReviewDecisionCount = await _database.JobReviewDecisions
            .ExecuteDeleteAsync(cancellationToken);
        var deletedJobCount = await _database.Jobs.ExecuteDeleteAsync(cancellationToken);
        var deletedRecurringSeriesCount = await _database.RecurringJobSeries
            .ExecuteDeleteAsync(cancellationToken);

        var reset = new HouseholdDataReset(
            Guid.NewGuid(),
            initiatedByAdultId,
            occurredAtUtc,
            deletedJobCount,
            deletedRecurringSeriesCount,
            deletedReviewDecisionCount,
            deletedPointsEntryCount);
        await _database.HouseholdDataResets.AddAsync(reset, cancellationToken);
        await _database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AdminDataResetResult(
            reset.Id,
            reset.OccurredAtUtc,
            reset.DeletedJobCount,
            reset.DeletedRecurringSeriesCount,
            reset.DeletedReviewDecisionCount,
            reset.DeletedPointsEntryCount);
    }
}
