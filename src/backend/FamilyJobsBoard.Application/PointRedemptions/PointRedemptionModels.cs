namespace FamilyJobsBoard.Application.PointRedemptions;

public sealed record RedeemPoints(
    Guid RequestId,
    Guid RedeemedByMemberId,
    Guid ChildId,
    int Points,
    string? Reward);

public sealed record RecordedPointRedemption(
    Guid Id,
    Guid ChildId,
    Guid RedeemedByMemberId,
    int Points,
    string Reward,
    DateTimeOffset RedeemedAtUtc);

public sealed record PointRedemptionResult(
    RecordedPointRedemption Redemption,
    int PointsBalance,
    bool WasCreated);
