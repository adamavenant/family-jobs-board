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
    Guid ChildId,
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

public sealed record GoodBehaviourLogResult(
    LoggedGoodBehaviour Behaviour,
    int PointsBalance,
    bool WasCreated);
