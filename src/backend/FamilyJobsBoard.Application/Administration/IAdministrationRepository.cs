namespace FamilyJobsBoard.Application.Administration;

public interface IAdministrationRepository
{
    Task<AdminDataResetResult> ResetJobsAndPointsAsync(
        Guid initiatedByAdultId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken);
}
