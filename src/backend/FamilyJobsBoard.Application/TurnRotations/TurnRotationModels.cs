namespace FamilyJobsBoard.Application.TurnRotations;

public sealed record TurnRotationAnswer(
    Guid RotationId,
    DateOnly Date,
    string Question,
    Guid ChildId,
    string ChildDisplayName);

public sealed record TurnRotationConfiguration(
    Guid Id,
    DateOnly EffectiveFrom,
    string Question,
    IReadOnlyList<Guid> ParticipantChildIds,
    Guid FirstChildId,
    Guid CreatedByMemberId,
    DateTimeOffset CreatedAtUtc);

public sealed record TurnRotationSummary(
    Guid RotationId,
    TurnRotationConfiguration? Current,
    IReadOnlyList<TurnRotationAnswer> UpcomingTurns);

public sealed record TurnRotationOverview(IReadOnlyList<TurnRotationSummary> Rotations);

public sealed record SaveTurnRotation(
    IReadOnlyList<Guid>? ParticipantChildIds,
    Guid? FirstChildId,
    DateOnly? EffectiveFrom,
    string? Question);
