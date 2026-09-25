namespace FamilyJobsBoard.Application.TurnRotations;

public sealed record TurnRotationAnswer(DateOnly Date, string Question, Guid ChildId);

public sealed record TurnRotationConfiguration(
    Guid Id,
    DateOnly EffectiveFrom,
    string Question,
    IReadOnlyList<Guid> ParticipantChildIds,
    Guid FirstChildId,
    Guid CreatedByMemberId,
    DateTimeOffset CreatedAtUtc);

public sealed record TurnRotationOverview(
    TurnRotationConfiguration? Current,
    IReadOnlyList<TurnRotationAnswer> UpcomingTurns);

public sealed record SaveTurnRotation(
    IReadOnlyList<Guid>? ParticipantChildIds,
    Guid? FirstChildId,
    DateOnly? EffectiveFrom,
    string? Question);
