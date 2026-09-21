using FamilyJobsBoard.Application.Clock;
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
        var resultingBalance = currentBalance + request.Amount;
        if (request.Amount < 0 && resultingBalance < 0 && !request.ConfirmNegativeBalance)
        {
            throw new NegativeBalanceConfirmationRequiredException(
                child.DisplayName,
                currentBalance,
                resultingBalance);
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
            var winner = await _repository.GetAdjustmentByRequestAsync(
                request.RequestId,
                cancellationToken)
                ?? throw new PointAdjustmentRequestConflictException(request.RequestId);
            return await ReplayAsync(winner, request, cancellationToken);
        }

        return new PointAdjustmentResult(
            Map(adjustment),
            await _repository.GetPointsBalanceAsync(child.Id, cancellationToken),
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

        return new PointAdjustmentResult(
            Map(existing),
            await _repository.GetPointsBalanceAsync(existing.ChildId, cancellationToken),
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
