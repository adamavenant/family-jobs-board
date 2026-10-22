using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Domain.TurnRotations;

namespace FamilyJobsBoard.Application.TurnRotations;

public sealed class TurnRotationService
{
    private const int PreviewDays = 14;
    private const string DailyMode = "daily";
    private const string WhenTickedMode = "whenTicked";

    private readonly ITurnRotationRepository _repository;
    private readonly IHouseholdClock _clock;

    public TurnRotationService(ITurnRotationRepository repository, IHouseholdClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public Task<IReadOnlyList<TurnRotationAnswer>> GetTurnsAsync(
        DateOnly date,
        CancellationToken cancellationToken) =>
        GetTurnsAsync(date, null, cancellationToken);

    /// <summary>
    /// Every rotation's answer for <paramref name="date"/>. With a <paramref name="viewer"/>,
    /// today's answers also say whether that viewer may tick the turn off or untick the latest
    /// tick.
    /// </summary>
    public async Task<IReadOnlyList<TurnRotationAnswer>> GetTurnsAsync(
        DateOnly date,
        TurnRotationViewer? viewer,
        CancellationToken cancellationToken)
    {
        var revisions = await _repository.GetRevisionsAsync(cancellationToken);
        var takenTurns = await LoadTakenTurnsAsync(revisions, cancellationToken);
        var names = await LoadNamesAsync(revisions, cancellationToken);
        return Groups(revisions)
            .Select(group => ResolveTurn(group, date, _clock.Today, takenTurns, names, viewer))
            .OfType<TurnRotationAnswer>()
            .ToArray();
    }

    public async Task<TurnRotationOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var revisions = await _repository.GetRevisionsAsync(cancellationToken);
        return await BuildOverviewAsync(revisions, cancellationToken);
    }

    /// <summary>
    /// Creates a replacement revision in every rotation, effective tomorrow, that leaves
    /// out a deactivated child, keeping the next assignee unchanged where possible.
    /// Earlier dates keep their answers, and restoring the child does not re-add them.
    /// </summary>
    public async Task RemoveParticipantAsync(
        Guid childId,
        Guid actorMemberId,
        CancellationToken cancellationToken)
    {
        var revisions = await _repository.GetRevisionsAsync(cancellationToken);
        var takenTurns = await LoadTakenTurnsAsync(revisions, cancellationToken);
        var effectiveFrom = _clock.Today.AddDays(1);
        var changed = false;
        foreach (var group in Groups(revisions))
        {
            var applicable = ResolveRevision(group, effectiveFrom);
            if (applicable is null || !applicable.OrderedParticipantChildIds.Contains(childId))
            {
                continue;
            }

            var order = applicable.OrderedParticipantChildIds;
            var remaining = order.Where(id => id != childId).ToArray();
            TurnRotationRevision replacement;
            if (remaining.Length == 0)
            {
                replacement = TurnRotationRevision.Cleared(
                    Guid.NewGuid(), applicable.RotationId, effectiveFrom, applicable.Question,
                    actorMemberId, _clock.UtcNow);
            }
            else
            {
                var next = applicable.GetTurn(effectiveFrom, _clock.Today, takenTurns)!.Value.ChildId;
                if (next == childId)
                {
                    var position = order.ToList().IndexOf(childId);
                    next = order.Skip(position + 1).Concat(order.Take(position))
                        .First(id => id != childId);
                }

                replacement = new TurnRotationRevision(
                    Guid.NewGuid(), applicable.RotationId, effectiveFrom, applicable.Question,
                    remaining, next, actorMemberId, _clock.UtcNow, applicable.AdvanceMode);
            }

            await _repository.AddRevisionAsync(replacement, cancellationToken);
            changed = true;
        }

        if (changed)
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Ends a rotation from tomorrow; earlier dates keep their answers.</summary>
    public async Task<TurnRotationOverview> EndAsync(
        Guid actorMemberId,
        Guid rotationId,
        CancellationToken cancellationToken)
    {
        await EnsureAdultAsync(actorMemberId, cancellationToken);
        var revisions = await _repository.GetRevisionsAsync(cancellationToken);
        var group = Groups(revisions).FirstOrDefault(item => item[0].RotationId == rotationId);
        if (group is null)
        {
            throw new TurnRotationNotFoundException(rotationId);
        }

        var tomorrow = _clock.Today.AddDays(1);
        var ended = new List<TurnRotationRevision>();
        // Supersede every scheduled configuration too, so a later edit cannot
        // bring the rotation back after End. Keep the original revisions intact.
        var effectiveDates = group.Select(revision => revision.EffectiveFrom)
            .Where(date => date >= tomorrow)
            .Append(tomorrow)
            .Distinct();
        foreach (var date in effectiveDates)
        {
            var applicable = ResolveRevision(group, date);
            if (applicable is null || applicable.IsCleared)
            {
                continue;
            }

            var revision = TurnRotationRevision.Cleared(
                Guid.NewGuid(), rotationId, date, applicable.Question,
                actorMemberId, _clock.UtcNow);
            ended.Add(revision);
            await _repository.AddRevisionAsync(revision, cancellationToken);
        }

        // A retry of End is successful without adding duplicate revisions.
        if (ended.Count > 0)
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        return await BuildOverviewAsync([.. revisions, .. ended], cancellationToken);
    }

    /// <summary>
    /// Saves a new revision. A null <paramref name="rotationId"/> starts a brand-new rotation.
    /// </summary>
    public async Task<TurnRotationOverview> SaveAsync(
        Guid actorMemberId,
        Guid? rotationId,
        SaveTurnRotation request,
        CancellationToken cancellationToken)
    {
        await EnsureAdultAsync(actorMemberId, cancellationToken);

        var revisions = await _repository.GetRevisionsAsync(cancellationToken);
        var isNew = rotationId is null;
        if (!isNew)
        {
            var group = Groups(revisions).FirstOrDefault(item => item[0].RotationId == rotationId);
            var latest = group is null ? null : ResolveRevision(group, DateOnly.MaxValue);
            var applicable = group is null ? null : ResolveRevision(group, _clock.Today.AddDays(1));
            if (group is null || latest is null || latest.IsCleared || (applicable?.IsCleared ?? false))
            {
                throw new TurnRotationNotFoundException(rotationId!.Value);
            }
        }

        var errors = new Dictionary<string, string[]>();
        var submittedIds = request.ParticipantChildIds ?? [];
        var distinctIds = submittedIds.Distinct().ToArray();
        if (distinctIds.Length == 0 || submittedIds.Count != distinctIds.Length
            || distinctIds.Any(id => id == Guid.Empty))
        {
            errors[nameof(SaveTurnRotation.ParticipantChildIds)] =
                ["Choose one or more different children."];
        }

        var children = distinctIds.Length == 0
            ? []
            : await _repository.GetActiveChildrenAsync(distinctIds, cancellationToken);
        if (distinctIds.Length > 0 && children.Count != distinctIds.Length)
        {
            errors[nameof(SaveTurnRotation.ParticipantChildIds)] =
                ["Choose active children in this household."];
        }

        if (request.FirstChildId is null
            || request.FirstChildId == Guid.Empty
            || !distinctIds.Contains(request.FirstChildId.Value))
        {
            errors[nameof(SaveTurnRotation.FirstChildId)] =
                ["Choose the child who takes the first turn from the selected participants."];
        }

        var minimumEffectiveFrom = isNew ? _clock.Today : _clock.Today.AddDays(1);
        if (request.EffectiveFrom is null)
        {
            errors[nameof(SaveTurnRotation.EffectiveFrom)] = ["Choose an effective date."];
        }
        else if (request.EffectiveFrom < minimumEffectiveFrom)
        {
            errors[nameof(SaveTurnRotation.EffectiveFrom)] =
                [$"The effective date must be on or after {minimumEffectiveFrom:yyyy-MM-dd}."];
        }

        if (request.Question?.Trim().Length > TurnRotationRevision.MaximumQuestionLength)
        {
            errors[nameof(SaveTurnRotation.Question)] =
                [$"The question must be {TurnRotationRevision.MaximumQuestionLength} characters or fewer."];
        }

        if (!TryParseAdvanceMode(request.AdvanceMode, out var advanceMode))
        {
            errors[nameof(SaveTurnRotation.AdvanceMode)] =
                [$"Choose whether the rotation moves on '{DailyMode}' or '{WhenTickedMode}'."];
        }

        if (errors.Count > 0)
        {
            throw new InvalidTurnRotationException(errors);
        }

        var revision = new TurnRotationRevision(
            Guid.NewGuid(),
            rotationId ?? Guid.NewGuid(),
            request.EffectiveFrom!.Value,
            request.Question,
            distinctIds,
            request.FirstChildId!.Value,
            actorMemberId,
            _clock.UtcNow,
            advanceMode);

        await _repository.AddRevisionAsync(revision, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return await BuildOverviewAsync([.. revisions, revision], cancellationToken);
    }

    /// <summary>
    /// Ticks off today's turn in a rotation that moves on when ticked. Any adult, or the child
    /// whose turn it is, may tick it. The request names the turn it saw by revision and turn
    /// number, so a stale tick is refused even when the same child's turn has come round
    /// again, and a replayed request returns the original tick instead of ticking twice.
    /// </summary>
    public async Task<TakenTurnDetails> TakeTurnAsync(
        Guid actorMemberId,
        Guid rotationId,
        TakeTurn request,
        CancellationToken cancellationToken)
    {
        if (request.RequestId == Guid.Empty)
        {
            throw new InvalidTurnRotationException(new Dictionary<string, string[]>
            {
                [nameof(TakeTurn.RequestId)] = ["Send a request ID with the tick."],
            });
        }

        await using var rotationLock = await _repository.LockRotationAsync(rotationId, cancellationToken);
        var replayed = await _repository.GetTakenTurnByRequestAsync(request.RequestId, cancellationToken);
        if (replayed is not null)
        {
            return await ReplayTakenTurnAsync(replayed, actorMemberId, rotationId, request, cancellationToken);
        }

        var today = _clock.Today;
        var revisions = await _repository.GetRevisionsAsync(cancellationToken);
        var group = revisions.Where(revision => revision.RotationId == rotationId).ToArray();
        if (group.Length == 0)
        {
            throw new TurnRotationNotFoundException(rotationId);
        }

        var applicable = ResolveRevision(group, today)
            ?? throw new TakenTurnConflictException("This rotation hasn't started yet.");
        if (applicable.IsCleared)
        {
            throw new TurnRotationNotFoundException(rotationId);
        }

        if (applicable.AdvanceMode != TurnAdvanceMode.WhenTicked)
        {
            throw new InvalidTurnRotationException(new Dictionary<string, string[]>
            {
                [nameof(SaveTurnRotation.AdvanceMode)] =
                    ["This rotation moves on every day, so its turns aren't ticked off."],
            });
        }

        var errors = new Dictionary<string, string[]>();
        var childId = request.ChildId ?? Guid.Empty;
        if (childId == Guid.Empty)
        {
            errors[nameof(TakeTurn.ChildId)] = ["Choose whose turn is being ticked off."];
        }

        var revisionId = request.RevisionId ?? Guid.Empty;
        if (revisionId == Guid.Empty)
        {
            errors[nameof(TakeTurn.RevisionId)] = ["Say which revision's turn is being ticked off."];
        }

        var turnNumber = request.TurnNumber ?? -1;
        if (turnNumber < 0)
        {
            errors[nameof(TakeTurn.TurnNumber)] = ["Say which turn is being ticked off."];
        }

        if (errors.Count > 0)
        {
            throw new InvalidTurnRotationException(errors);
        }

        var actor = await _repository.GetMemberAsync(actorMemberId, cancellationToken);
        if (actor is not { IsActive: true } || (!actor.IsAdult && actor.Id != childId))
        {
            throw new TakenTurnForbiddenException();
        }

        var takenTurns = await _repository.GetTakenTurnsAsync([applicable.Id], cancellationToken);
        var names = await LoadNamesAsync([applicable], cancellationToken);
        var turn = applicable.GetTurn(today, today, takenTurns)!.Value;
        if (turn.IsTaken)
        {
            throw new TakenTurnConflictException(
                $"{Name(names, turn.ChildId)}'s turn has already been ticked off today.");
        }

        if (revisionId != applicable.Id || turnNumber != turn.Number || childId != turn.ChildId)
        {
            throw new TakenTurnConflictException(
                $"That turn has already moved on. It's {Name(names, turn.ChildId)}'s turn now.");
        }

        var taken = applicable.TakeTurn(
            Guid.NewGuid(),
            request.RequestId,
            today,
            turnNumber,
            childId,
            takenTurns,
            actor.Id,
            ToStoredPrecision(_clock.UtcNow));
        await _repository.AddTakenTurnAsync(taken, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        await rotationLock.CommitAsync(cancellationToken);
        return MapTakenTurn(taken, names);
    }

    /// <summary>
    /// Unticks a rotation's most recent tick on the day it was made, restoring the previous
    /// answer. Any adult, or the child whose turn was ticked, may untick it; the tick is kept
    /// with who unticked it and when.
    /// </summary>
    public async Task<TakenTurnDetails> UndoTurnAsync(
        Guid actorMemberId,
        Guid rotationId,
        Guid takenTurnId,
        CancellationToken cancellationToken)
    {
        await using var rotationLock = await _repository.LockRotationAsync(rotationId, cancellationToken);
        var taken = await _repository.FindTakenTurnAsync(takenTurnId, cancellationToken);
        if (taken is null || taken.RotationId != rotationId)
        {
            throw new TakenTurnNotFoundException(takenTurnId);
        }

        var actor = await _repository.GetMemberAsync(actorMemberId, cancellationToken);
        if (actor is not { IsActive: true } || (!actor.IsAdult && actor.Id != taken.ChildId))
        {
            throw new TakenTurnForbiddenException();
        }

        if (taken.IsUndone)
        {
            throw new TakenTurnConflictException("That turn has already been unticked.");
        }

        if (taken.Date != _clock.Today)
        {
            throw new TakenTurnConflictException("Only a turn ticked off today can be unticked.");
        }

        var revisions = await _repository.GetRevisionsAsync(cancellationToken);
        var revision = revisions.Single(item => item.Id == taken.RevisionId);
        var takenTurns = await _repository.GetTakenTurnsAsync([revision.Id], cancellationToken);
        if (revision.LatestTakenTurn(takenTurns)?.Id != taken.Id)
        {
            throw new TakenTurnConflictException("Only the most recent tick can be unticked.");
        }

        taken.Undo(actor.Id, _clock.UtcNow);
        await _repository.SaveChangesAsync(cancellationToken);
        await rotationLock.CommitAsync(cancellationToken);
        return MapTakenTurn(taken, await LoadNamesAsync([revision], cancellationToken));
    }

    /// <summary>
    /// Answers a request that was already recorded with its original tick, without ticking
    /// again. A request ID reused for a different tick is a conflict.
    /// </summary>
    private async Task<TakenTurnDetails> ReplayTakenTurnAsync(
        TakenTurn replayed,
        Guid actorMemberId,
        Guid rotationId,
        TakeTurn request,
        CancellationToken cancellationToken)
    {
        var samePayload = replayed.RotationId == rotationId
            && replayed.RecordedByMemberId == actorMemberId
            && replayed.RevisionId == request.RevisionId
            && replayed.TurnNumber == request.TurnNumber
            && replayed.ChildId == request.ChildId;
        if (!samePayload)
        {
            throw new TakenTurnConflictException("That request ID was already used for a different tick.");
        }

        var revisions = await _repository.GetRevisionsAsync(cancellationToken);
        var names = await LoadNamesAsync(
            revisions.Where(revision => revision.Id == replayed.RevisionId).ToArray(),
            cancellationToken);
        return MapTakenTurn(replayed, names);
    }

    /// <summary>
    /// Drops sub-microsecond ticks, which PostgreSQL does not keep, so the answer to a tick
    /// matches what a replay of the same request later reads back.
    /// </summary>
    private static DateTimeOffset ToStoredPrecision(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));

    private async Task EnsureAdultAsync(Guid actorMemberId, CancellationToken cancellationToken)
    {
        var actor = await _repository.GetMemberAsync(actorMemberId, cancellationToken);
        if (actor is not { IsAdult: true })
        {
            throw new TurnRotationForbiddenException();
        }
    }

    private async Task<TurnRotationOverview> BuildOverviewAsync(
        IReadOnlyList<TurnRotationRevision> revisions,
        CancellationToken cancellationToken)
    {
        var today = _clock.Today;
        var takenTurns = await LoadTakenTurnsAsync(revisions, cancellationToken);
        var names = await LoadNamesAsync(revisions, cancellationToken);
        var summaries = new List<TurnRotationSummary>();
        foreach (var group in Groups(revisions))
        {
            var current = ResolveRevision(group, today);
            if (current is { IsCleared: true }
                || (current is null && ResolveRevision(group, DateOnly.MaxValue) is { IsCleared: true }))
            {
                continue;
            }

            // A future date in a rotation that moves on when ticked can't be predicted, so the
            // preview leaves those dates out and lists the order from the next turn instead.
            var upcomingTurns = new List<TurnRotationAnswer>();
            for (var offset = 0; offset < PreviewDays; offset++)
            {
                var date = today.AddDays(offset);
                if (offset > 0 && ResolveRevision(group, date) is { AdvanceMode: TurnAdvanceMode.WhenTicked })
                {
                    continue;
                }

                if (ResolveTurn(group, date, today, takenTurns, names, null) is { } answer)
                {
                    upcomingTurns.Add(answer);
                }
            }

            var basis = current ?? group
                .Where(revision => revision.EffectiveFrom > today)
                .OrderBy(revision => revision.EffectiveFrom)
                .ThenBy(revision => revision.CreatedAtUtc)
                .FirstOrDefault();
            summaries.Add(new TurnRotationSummary(
                group[0].RotationId,
                current is null ? null : MapConfiguration(current),
                upcomingTurns,
                basis is { AdvanceMode: TurnAdvanceMode.WhenTicked, IsCleared: false }
                    ? UpNext(basis, today, takenTurns, names)
                    : []));
        }

        return new TurnRotationOverview(summaries);
    }

    /// <summary>Revisions grouped per rotation, oldest rotation first.</summary>
    private static List<IReadOnlyList<TurnRotationRevision>> Groups(
        IReadOnlyList<TurnRotationRevision> revisions)
    {
        return revisions
            .GroupBy(revision => revision.RotationId)
            .OrderBy(group => group.Min(revision => revision.CreatedAtUtc))
            .ThenBy(group => group.Key)
            .Select(group => (IReadOnlyList<TurnRotationRevision>)group.ToArray())
            .ToList();
    }

    private async Task<IReadOnlyList<TakenTurn>> LoadTakenTurnsAsync(
        IReadOnlyList<TurnRotationRevision> revisions,
        CancellationToken cancellationToken)
    {
        var ids = revisions
            .Where(revision => revision.AdvanceMode == TurnAdvanceMode.WhenTicked)
            .Select(revision => revision.Id)
            .ToArray();
        return ids.Length == 0 ? [] : await _repository.GetTakenTurnsAsync(ids, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadNamesAsync(
        IReadOnlyList<TurnRotationRevision> revisions,
        CancellationToken cancellationToken)
    {
        var ids = revisions.SelectMany(revision => revision.OrderedParticipantChildIds)
            .Distinct()
            .ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var members = await _repository.GetMembersAsync(ids, cancellationToken);
        return members.ToDictionary(member => member.Id, member => member.DisplayName);
    }

    private static TurnRotationAnswer? ResolveTurn(
        IReadOnlyList<TurnRotationRevision> revisions,
        DateOnly date,
        DateOnly today,
        IReadOnlyList<TakenTurn> takenTurns,
        IReadOnlyDictionary<Guid, string> names,
        TurnRotationViewer? viewer)
    {
        var revision = ResolveRevision(revisions, date);
        if (revision?.GetTurn(date, today, takenTurns) is not { } turn)
        {
            return null;
        }

        var tickable = revision.AdvanceMode == TurnAdvanceMode.WhenTicked && date == today;
        var latest = tickable ? revision.LatestTakenTurn(takenTurns) : null;
        return new TurnRotationAnswer(
            revision.RotationId,
            date,
            revision.Question,
            turn.ChildId,
            Name(names, turn.ChildId),
            revision.Id,
            turn.Number,
            MapAdvanceMode(revision.AdvanceMode),
            turn.IsTaken,
            tickable && !turn.IsTaken && MayTick(viewer, turn.ChildId),
            latest is not null && latest.Date == today && MayTick(viewer, latest.ChildId)
                ? new TurnRotationTakenTurn(latest.Id, latest.ChildId, Name(names, latest.ChildId))
                : null);
    }

    private static bool MayTick(TurnRotationViewer? viewer, Guid childId) =>
        viewer is not null && (viewer.IsAdult || viewer.MemberId == childId);

    /// <summary>The participants in turn order, starting with whoever takes the next turn.</summary>
    private static IReadOnlyList<TurnRotationChild> UpNext(
        TurnRotationRevision revision,
        DateOnly today,
        IReadOnlyList<TakenTurn> takenTurns,
        IReadOnlyDictionary<Guid, string> names)
    {
        var from = revision.EffectiveFrom > today ? revision.EffectiveFrom : today.AddDays(1);
        var next = revision.GetTurn(from, today, takenTurns)!.Value.ChildId;
        var order = revision.OrderedParticipantChildIds;
        var position = order.ToList().IndexOf(next);
        return order.Skip(position).Concat(order.Take(position))
            .Select(childId => new TurnRotationChild(childId, Name(names, childId)))
            .ToArray();
    }

    private static TurnRotationRevision? ResolveRevision(
        IReadOnlyList<TurnRotationRevision> revisions,
        DateOnly date)
    {
        return revisions
            .Where(revision => revision.EffectiveFrom <= date)
            .OrderByDescending(revision => revision.EffectiveFrom)
            .ThenByDescending(revision => revision.CreatedAtUtc)
            .ThenByDescending(revision => revision.IsCleared)
            .FirstOrDefault();
    }

    private static string Name(IReadOnlyDictionary<Guid, string> names, Guid childId) =>
        names.GetValueOrDefault(childId, "Unknown");

    private static TurnRotationConfiguration MapConfiguration(TurnRotationRevision revision)
    {
        return new TurnRotationConfiguration(
            revision.Id,
            revision.EffectiveFrom,
            revision.Question,
            revision.OrderedParticipantChildIds,
            revision.FirstChildId!.Value,
            MapAdvanceMode(revision.AdvanceMode),
            revision.CreatedByMemberId,
            revision.CreatedAtUtc);
    }

    private static TakenTurnDetails MapTakenTurn(TakenTurn taken, IReadOnlyDictionary<Guid, string> names)
    {
        return new TakenTurnDetails(
            taken.Id,
            taken.RequestId,
            taken.RotationId,
            taken.RevisionId,
            taken.TurnNumber,
            taken.Date,
            taken.ChildId,
            Name(names, taken.ChildId),
            taken.RecordedByMemberId,
            taken.RecordedAtUtc,
            taken.UndoneByMemberId,
            taken.UndoneAtUtc);
    }

    private static string MapAdvanceMode(TurnAdvanceMode advanceMode) => advanceMode switch
    {
        TurnAdvanceMode.Daily => DailyMode,
        TurnAdvanceMode.WhenTicked => WhenTickedMode,
        _ => throw new InvalidOperationException($"Unknown advance mode '{advanceMode}'."),
    };

    private static bool TryParseAdvanceMode(string? value, out TurnAdvanceMode advanceMode)
    {
        advanceMode = value switch
        {
            WhenTickedMode => TurnAdvanceMode.WhenTicked,
            _ => TurnAdvanceMode.Daily,
        };
        return string.IsNullOrEmpty(value) || value is DailyMode or WhenTickedMode;
    }
}
