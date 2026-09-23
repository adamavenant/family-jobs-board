namespace FamilyJobsBoard.Application.PointAdjustments;

public sealed record RecordPointAdjustment(
    Guid RequestId,
    Guid AdjustedByMemberId,
    Guid ChildId,
    int Amount,
    string? Reason,
    bool ConfirmNegativeBalance);

public sealed record RecordedPointAdjustment(
    Guid Id,
    Guid ChildId,
    Guid AdjustedByMemberId,
    int Amount,
    string Reason,
    DateTimeOffset AdjustedAtUtc);

public sealed record PointAdjustmentResult(
    RecordedPointAdjustment Adjustment,
    int PointsBalance,
    bool WasCreated);
