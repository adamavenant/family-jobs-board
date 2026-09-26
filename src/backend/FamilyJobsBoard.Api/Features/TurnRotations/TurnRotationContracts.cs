namespace FamilyJobsBoard.Api.Features.TurnRotations;

public sealed record TurnRotationConfigurationResponse(
    Guid Id,
    DateOnly EffectiveFrom,
    string Question,
    IReadOnlyList<Guid> ParticipantChildIds,
    Guid FirstChildId,
    Guid CreatedByMemberId,
    DateTimeOffset CreatedAtUtc);

public sealed record TurnRotationTurnResponse(
    DateOnly Date,
    string Question,
    Guid ChildId,
    string ChildDisplayName);

public sealed record TurnRotationOverviewResponse(
    TurnRotationConfigurationResponse? Current,
    IReadOnlyList<TurnRotationTurnResponse> UpcomingTurns,
    bool HasRevisions);

public sealed record SaveTurnRotationRequest(
    IReadOnlyList<Guid>? ParticipantChildIds,
    Guid? FirstChildId,
    DateOnly? EffectiveFrom,
    string? Question);
