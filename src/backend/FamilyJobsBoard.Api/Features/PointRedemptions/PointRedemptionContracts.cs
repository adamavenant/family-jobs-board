namespace FamilyJobsBoard.Api.Features.PointRedemptions;

public sealed record RedeemPointsRequest(
    Guid RequestId,
    Guid ChildId,
    int Points,
    string? Reward);

public sealed record PointRedemptionResponse(
    Guid Id,
    Guid ChildId,
    Guid RedeemedByMemberId,
    int Points,
    string Reward,
    DateTimeOffset RedeemedAtUtc);

public sealed record RedeemPointsResponse(
    PointRedemptionResponse Redemption,
    int PointsBalance);
