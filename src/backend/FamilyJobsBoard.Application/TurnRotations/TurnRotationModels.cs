namespace FamilyJobsBoard.Application.TurnRotations;

/// <summary>
/// A rotation's answer for a date. <see cref="RevisionId"/> and <see cref="TurnNumber"/>
/// identify the turn shown, so a tick can name exactly which turn it takes.
/// </summary>
public sealed record TurnRotationAnswer(
    Guid RotationId,
    DateOnly Date,
    string Question,
    Guid ChildId,
    string ChildDisplayName,
    Guid RevisionId,
    int TurnNumber,
    string AdvanceMode,
    bool TurnTaken,
    bool CanTakeTurn,
    TurnRotationTakenTurn? UndoableTurn);

/// <summary>A tick shown on the board that the viewer may untick.</summary>
public sealed record TurnRotationTakenTurn(
    Guid TurnId,
    Guid ChildId,
    string ChildDisplayName);

/// <summary>Who is looking at the board, so it can offer only the ticks they may make.</summary>
public sealed record TurnRotationViewer(Guid MemberId, bool IsAdult);

public sealed record TurnRotationConfiguration(
    Guid Id,
    DateOnly EffectiveFrom,
    string Question,
    IReadOnlyList<Guid> ParticipantChildIds,
    Guid FirstChildId,
    string AdvanceMode,
    Guid CreatedByMemberId,
    DateTimeOffset CreatedAtUtc);

public sealed record TurnRotationChild(Guid ChildId, string ChildDisplayName);

public sealed record TurnRotationSummary(
    Guid RotationId,
    TurnRotationConfiguration? Current,
    IReadOnlyList<TurnRotationAnswer> UpcomingTurns,
    IReadOnlyList<TurnRotationChild> UpNext);

public sealed record TurnRotationOverview(IReadOnlyList<TurnRotationSummary> Rotations);

public sealed record SaveTurnRotation(
    IReadOnlyList<Guid>? ParticipantChildIds,
    Guid? FirstChildId,
    DateOnly? EffectiveFrom,
    string? Question,
    string? AdvanceMode = null);

/// <summary>
/// Ticks off the turn the board showed: <see cref="RevisionId"/> and <see cref="TurnNumber"/>
/// name it, and <see cref="RequestId"/> makes a replayed request return the original tick.
/// </summary>
public sealed record TakeTurn(Guid RequestId, Guid? RevisionId, int? TurnNumber, Guid? ChildId);

public sealed record TakenTurnDetails(
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
