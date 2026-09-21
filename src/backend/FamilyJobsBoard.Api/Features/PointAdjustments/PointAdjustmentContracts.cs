namespace FamilyJobsBoard.Api.Features.PointAdjustments;

public sealed record RecordPointAdjustmentRequest(
    Guid RequestId,
    Guid ChildId,
    int Amount,
    string? Reason,
    bool ConfirmNegativeBalance);

public sealed record PointAdjustmentResponse(
    Guid Id,
    Guid ChildId,
    Guid AdjustedByMemberId,
    int Amount,
    string Reason,
    DateTimeOffset AdjustedAtUtc);

public sealed record RecordPointAdjustmentResponse(
    PointAdjustmentResponse Adjustment,
    int PointsBalance);
