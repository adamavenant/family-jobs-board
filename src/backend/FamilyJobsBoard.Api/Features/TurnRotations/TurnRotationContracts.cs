namespace FamilyJobsBoard.Api.Features.TurnRotations;

public sealed record TurnRotationConfigurationResponse(
    Guid Id,
    DateOnly EffectiveFrom,
    string Question,
    IReadOnlyList<Guid> ParticipantChildIds,
    Guid FirstChildId,
    string AdvanceMode,
    Guid CreatedByMemberId,
    DateTimeOffset CreatedAtUtc);

public sealed record TurnRotationTurnResponse(
    DateOnly Date,
    string Question,
    Guid ChildId,
    string ChildDisplayName);

public sealed record TurnRotationChildResponse(Guid ChildId, string ChildDisplayName);

public sealed record TurnRotationSummaryResponse(
    Guid RotationId,
    TurnRotationConfigurationResponse? Current,
    IReadOnlyList<TurnRotationTurnResponse> UpcomingTurns,
    IReadOnlyList<TurnRotationChildResponse> UpNext,
    bool EndsTomorrow);

public sealed record TurnRotationOverviewResponse(
    IReadOnlyList<TurnRotationSummaryResponse> Rotations);

public sealed record SaveTurnRotationRequest(
    IReadOnlyList<Guid>? ParticipantChildIds,
    Guid? FirstChildId,
    DateOnly? EffectiveFrom,
    string? Question,
    string? AdvanceMode);

public sealed record TakeTurnRequest(
    Guid? RequestId,
    Guid? RevisionId,
    int? TurnNumber,
    Guid? ChildId);

public sealed record TakenTurnResponse(
    Guid Id,
    Guid RequestId,
    Guid RotationId,
    Guid RevisionId,
    int TurnNumber,
    DateOnly Date,
    Guid ChildId,
    string ChildDisplayName,
    Guid RecordedByMemberId,
    DateTimeOffset RecordedAtUtc,
    Guid? UndoneByMemberId,
    DateTimeOffset? UndoneAtUtc);
