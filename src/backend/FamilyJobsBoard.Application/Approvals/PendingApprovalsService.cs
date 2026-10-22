using FamilyJobsBoard.Application.Today;

namespace FamilyJobsBoard.Application.Approvals;

/// <summary>
/// The household's review queue: every job awaiting an adult's decision, whatever date it was
/// scheduled for. Authorization (adult-only) is enforced by the endpoint's policy, matching the
/// calendar; decisions themselves still go through <see cref="TodayBoardService"/>.
/// </summary>
public sealed class PendingApprovalsService
{
    private readonly ITodayBoardRepository _repository;

    public PendingApprovalsService(ITodayBoardRepository repository)
    {
        _repository = repository;
    }

    public async Task<PendingApprovals> GetAsync(CancellationToken cancellationToken)
    {
        var members = await _repository.GetMembersAsync(cancellationToken);
        var childById = members
            .Where(member => !member.IsAdult)
            .ToDictionary(child => child.Id);
        var jobs = await _repository.GetPendingApprovalJobsAsync(
            childById.Keys.ToArray(),
            cancellationToken);

        return new PendingApprovals(jobs
            .Select(job => TodayJobMapping.MapJob(job, childById[job.ChildId], null))
            .ToArray());
    }
}

public sealed record PendingApprovals(IReadOnlyList<TodayJob> Jobs);
