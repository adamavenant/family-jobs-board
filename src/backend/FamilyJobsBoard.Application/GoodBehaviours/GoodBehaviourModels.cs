namespace FamilyJobsBoard.Application.GoodBehaviours;

public sealed record GoodBehaviourTypeSummary(
    Guid Id,
    string Name,
    string Description,
    int Points);

public sealed record SaveGoodBehaviourType(
    string? Name,
    string? Description,
    int Points);

public sealed record LogGoodBehaviour(
    Guid RequestId,
    Guid LoggedByMemberId,
    Guid TypeId,
    IReadOnlyList<Guid>? ChildIds,
    int? Points);

public sealed record LoggedGoodBehaviour(
    Guid Id,
    Guid TypeId,
    string TypeName,
    string TypeDescription,
    Guid ChildId,
    Guid LoggedByMemberId,
    int Points,
    DateTimeOffset LoggedAtUtc);

public sealed record GoodBehaviourAward(
    LoggedGoodBehaviour Behaviour,
    int PointsBalance);

public sealed record GoodBehaviourLogResult(
    IReadOnlyList<GoodBehaviourAward> Awards,
    bool WasCreated);
