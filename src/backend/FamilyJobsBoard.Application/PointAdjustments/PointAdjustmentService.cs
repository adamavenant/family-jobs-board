using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Application.Points;
using FamilyJobsBoard.Domain.PointAdjustments;
using FamilyJobsBoard.Domain.Points;

namespace FamilyJobsBoard.Application.PointAdjustments;

public sealed class PointAdjustmentService
{
    private readonly IPointAdjustmentRepository _repository;
    private readonly IHouseholdClock _clock;

    public PointAdjustmentService(IPointAdjustmentRepository repository, IHouseholdClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<PointAdjustmentResult> RecordAsync(
        RecordPointAdjustment request,
        CancellationToken cancellationToken)
    {
        if (request.RequestId == Guid.Empty)
        {
            throw Invalid(nameof(RecordPointAdjustment.RequestId), "A request ID is required.");
        }

        var existing = await _repository.GetAdjustmentByRequestAsync(
            request.RequestId,
            cancellationToken);
        if (existing is not null)
        {
            return await ReplayAsync(existing, request, cancellationToken);
        }

        var errors = new Dictionary<string, string[]>();
        if (request.Amount == 0)
        {
            errors[nameof(RecordPointAdjustment.Amount)] =
                ["Add or remove at least one point."];
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            errors[nameof(RecordPointAdjustment.Reason)] = ["Enter a reason."];
        }
        else if (reason.Length > PointAdjustment.MaximumReasonLength)
        {
            errors[nameof(RecordPointAdjustment.Reason)] =
                [$"The reason must be {PointAdjustment.MaximumReasonLength} characters or fewer."];
        }

        var recorded = await RecordUnderLockAsync(request, reason, errors, cancellationToken);
        if (recorded is not null)
        {
            return recorded;
        }

        // The request ID was taken by a concurrent request for another child, whose lock this
        // request didn't hold. The failed transaction has been rolled back, so read the winner.
        var winner = await _repository.GetAdjustmentByRequestAsync(
            request.RequestId,
            cancellationToken)
            ?? throw new PointAdjustmentRequestConflictException(request.RequestId);
        return await ReplayAsync(winner, request, cancellationToken);
    }

    /// <summary>
    /// Checks and records the adjustment while holding the child's points lock, so a removal
    /// can't take the balance below zero alongside a concurrent removal. Returns null when a
    /// concurrent request with the same request ID won the insert.
    /// </summary>
    private async Task<PointAdjustmentResult?> RecordUnderLockAsync(
        RecordPointAdjustment request,
        string? reason,
        Dictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        await using var pointsLock = await _repository.LockChildPointsAsync(
            request.ChildId,
            cancellationToken);

        // A retry may have been recorded while this request waited for the lock; replay it
        // rather than judge it against the balance it already changed.
        var existing = await _repository.GetAdjustmentByRequestAsync(
            request.RequestId,
            cancellationToken);
        if (existing is not null)
        {
            return await ReplayAsync(existing, request, cancellationToken);
        }

        var child = await _repository.GetActiveChildAsync(request.ChildId, cancellationToken);
        if (child is null)
        {
            errors[nameof(RecordPointAdjustment.ChildId)] =
                ["Choose an active child in this household."];
        }

        if (errors.Count > 0)
        {
            throw new InvalidPointAdjustmentException(errors);
        }

        var currentBalance = await _repository.GetPointsBalanceAsync(child!.Id, cancellationToken);
        if (request.Amount < 0 && currentBalance + request.Amount < 0)
        {
            throw new InsufficientPointsException(child.DisplayName, currentBalance);
        }

        var adjustedAtUtc = _clock.UtcNow;
        var adjustment = new PointAdjustment(
            Guid.NewGuid(),
            request.RequestId,
            child.Id,
            request.AdjustedByMemberId,
            request.Amount,
            reason!,
            adjustedAtUtc);
        var entry = PointsLedgerEntry.ForManualAdjustment(
            Guid.NewGuid(),
            child.Id,
            adjustment.Id,
            adjustment.Amount,
            adjustedAtUtc);

        await _repository.AddAdjustmentAsync(adjustment, entry, cancellationToken);
        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicatePointAdjustmentRequestException)
        {
            return null;
        }

        await pointsLock.CommitAsync(cancellationToken);
        return new PointAdjustmentResult(
            Map(adjustment),
            currentBalance + adjustment.Amount,
            WasCreated: true);
    }

    private async Task<PointAdjustmentResult> ReplayAsync(
        PointAdjustment existing,
        RecordPointAdjustment request,
        CancellationToken cancellationToken)
    {
        var samePayload = existing.ChildId == request.ChildId
            && existing.AdjustedByMemberId == request.AdjustedByMemberId
            && existing.Amount == request.Amount
            && string.Equals(existing.Reason, request.Reason?.Trim(), StringComparison.Ordinal);
        if (!samePayload)
        {
            throw new PointAdjustmentRequestConflictException(request.RequestId);
        }

        // The original result: the balance right after this adjustment, not today's balance.
        return new PointAdjustmentResult(
            Map(existing),
            await _repository.GetPointsBalanceAfterAdjustmentAsync(existing.Id, cancellationToken),
            WasCreated: false);
    }

    private static InvalidPointAdjustmentException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private static RecordedPointAdjustment Map(PointAdjustment adjustment) =>
        new(
            adjustment.Id,
            adjustment.ChildId,
            adjustment.AdjustedByMemberId,
            adjustment.Amount,
            adjustment.Reason,
            adjustment.AdjustedAtUtc);
}
