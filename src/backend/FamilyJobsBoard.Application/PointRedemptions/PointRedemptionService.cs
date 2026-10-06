using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Application.Points;
using FamilyJobsBoard.Domain.PointRedemptions;
using FamilyJobsBoard.Domain.Points;

namespace FamilyJobsBoard.Application.PointRedemptions;

public sealed class PointRedemptionService
{
    private readonly IPointRedemptionRepository _repository;
    private readonly IHouseholdClock _clock;

    public PointRedemptionService(IPointRedemptionRepository repository, IHouseholdClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<PointRedemptionResult> RedeemAsync(
        RedeemPoints request,
        CancellationToken cancellationToken)
    {
        if (request.RequestId == Guid.Empty)
        {
            throw Invalid(nameof(RedeemPoints.RequestId), "A request ID is required.");
        }

        var existing = await _repository.GetRedemptionByRequestAsync(
            request.RequestId,
            cancellationToken);
        if (existing is not null)
        {
            return await ReplayAsync(existing, request, cancellationToken);
        }

        var errors = new Dictionary<string, string[]>();
        if (request.Points < 1)
        {
            errors[nameof(RedeemPoints.Points)] = ["Redeem at least one point."];
        }

        var reward = request.Reward?.Trim();
        if (string.IsNullOrEmpty(reward))
        {
            errors[nameof(RedeemPoints.Reward)] = ["Enter the reward."];
        }
        else if (reward.Length > PointRedemption.MaximumRewardLength)
        {
            errors[nameof(RedeemPoints.Reward)] =
                [$"The reward must be {PointRedemption.MaximumRewardLength} characters or fewer."];
        }

        var recorded = await RecordUnderLockAsync(request, reward, errors, cancellationToken);
        if (recorded is not null)
        {
            return recorded;
        }

        // The request ID was taken by a concurrent request for another child, whose lock this
        // request didn't hold. The failed transaction has been rolled back, so read the winner.
        var winner = await _repository.GetRedemptionByRequestAsync(
            request.RequestId,
            cancellationToken)
            ?? throw new PointRedemptionRequestConflictException(request.RequestId);
        return await ReplayAsync(winner, request, cancellationToken);
    }

    /// <summary>
    /// Checks and records the redemption while holding the child's points lock. Returns null
    /// when a concurrent request with the same request ID won the insert.
    /// </summary>
    private async Task<PointRedemptionResult?> RecordUnderLockAsync(
        RedeemPoints request,
        string? reward,
        Dictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        await using var pointsLock = await _repository.LockChildPointsAsync(
            request.ChildId,
            cancellationToken);

        // A retry may have been recorded while this request waited for the lock; replay it
        // rather than judge it against the balance it already reduced.
        var existing = await _repository.GetRedemptionByRequestAsync(
            request.RequestId,
            cancellationToken);
        if (existing is not null)
        {
            return await ReplayAsync(existing, request, cancellationToken);
        }

        var child = await _repository.GetActiveChildAsync(request.ChildId, cancellationToken);
        if (child is null)
        {
            errors[nameof(RedeemPoints.ChildId)] = ["Choose an active child in this household."];
        }

        if (errors.Count > 0)
        {
            throw new InvalidPointRedemptionException(errors);
        }

        var currentBalance = await _repository.GetPointsBalanceAsync(child!.Id, cancellationToken);
        if (request.Points > currentBalance)
        {
            throw new InsufficientPointsException(child.DisplayName, currentBalance);
        }

        var redeemedAtUtc = _clock.UtcNow;
        var redemption = new PointRedemption(
            Guid.NewGuid(),
            request.RequestId,
            child.Id,
            request.RedeemedByMemberId,
            request.Points,
            reward!,
            redeemedAtUtc);
        var entry = PointsLedgerEntry.ForRedemption(
            Guid.NewGuid(),
            child.Id,
            redemption.Id,
            redemption.Points,
            redeemedAtUtc);

        await _repository.AddRedemptionAsync(redemption, entry, cancellationToken);
        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicatePointRedemptionRequestException)
        {
            return null;
        }

        await pointsLock.CommitAsync(cancellationToken);
        return new PointRedemptionResult(
            Map(redemption),
            currentBalance - redemption.Points,
            WasCreated: true);
    }

    private async Task<PointRedemptionResult> ReplayAsync(
        PointRedemption existing,
        RedeemPoints request,
        CancellationToken cancellationToken)
    {
        var samePayload = existing.ChildId == request.ChildId
            && existing.RedeemedByMemberId == request.RedeemedByMemberId
            && existing.Points == request.Points
            && string.Equals(existing.Reward, request.Reward?.Trim(), StringComparison.Ordinal);
        if (!samePayload)
        {
            throw new PointRedemptionRequestConflictException(request.RequestId);
        }

        // The original result: the balance right after this redemption, not today's balance.
        return new PointRedemptionResult(
            Map(existing),
            await _repository.GetPointsBalanceAfterRedemptionAsync(existing.Id, cancellationToken),
            WasCreated: false);
    }

    private static InvalidPointRedemptionException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private static RecordedPointRedemption Map(PointRedemption redemption) =>
        new(
            redemption.Id,
            redemption.ChildId,
            redemption.RedeemedByMemberId,
            redemption.Points,
            redemption.Reward,
            redemption.RedeemedAtUtc);
}
