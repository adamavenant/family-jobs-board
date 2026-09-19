using FamilyJobsBoard.Application.Clock;

namespace FamilyJobsBoard.Application.Administration;

public sealed class AdministrationService
{
    public const string RequiredConfirmation = "RESET TASKS AND POINTS";

    private readonly IAdministrationRepository _repository;
    private readonly IHouseholdClock _clock;

    public AdministrationService(
        IAdministrationRepository repository,
        IHouseholdClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<AdminDataResetResult> ResetJobsAndPointsAsync(
        Guid initiatedByAdultId,
        string? confirmation,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
            confirmation,
            RequiredConfirmation,
            StringComparison.Ordinal))
        {
            throw new InvalidAdminDataResetConfirmationException();
        }

        return await _repository.ResetJobsAndPointsAsync(
            initiatedByAdultId,
            _clock.UtcNow,
            cancellationToken);
    }
}
