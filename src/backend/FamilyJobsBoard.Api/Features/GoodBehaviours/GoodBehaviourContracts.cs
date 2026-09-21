namespace FamilyJobsBoard.Api.Features.GoodBehaviours;

public sealed record GoodBehaviourTypeResponse(
    Guid Id,
    string Name,
    string Description,
    int Points);

public sealed record GoodBehaviourTypesResponse(
    IReadOnlyList<GoodBehaviourTypeResponse> Types);

public sealed record SaveGoodBehaviourTypeRequest(
    string? Name,
    string? Description,
    int Points);

public sealed record LogGoodBehaviourRequest(
    Guid RequestId,
    Guid TypeId,
    Guid ChildId,
    int? Points);

public sealed record GoodBehaviourResponse(
    Guid Id,
    Guid TypeId,
    string TypeName,
    string TypeDescription,
    Guid ChildId,
    Guid LoggedByMemberId,
    int Points,
    DateTimeOffset LoggedAtUtc);

public sealed record LogGoodBehaviourResponse(
    GoodBehaviourResponse Behaviour,
    int PointsBalance);
