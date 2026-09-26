using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Domain.TurnRotations;

namespace FamilyJobsBoard.Application.TurnRotations;

public sealed class TurnRotationService
{
    private const int PreviewDays = 14;

    private readonly ITurnRotationRepository _repository;
    private readonly IHouseholdClock _clock;

    public TurnRotationService(ITurnRotationRepository repository, IHouseholdClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<IReadOnlyList<TurnRotationAnswer>> GetTurnsAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var revisions = await _repository.GetRevisionsAsync(cancellationToken);
        var names = await LoadNamesAsync(revisions, cancellationToken);
        return Groups(revisions)
            .Select(group => ResolveTurn(group, date, names))
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
                var next = applicable.GetAssignedChildId(effectiveFrom)!.Value;
                if (next == childId)
                {
                    var position = order.ToList().IndexOf(childId);
                    next = order.Skip(position + 1).Concat(order.Take(position))
                        .First(id => id != childId);
                }

                replacement = new TurnRotationRevision(
                    Guid.NewGuid(), applicable.RotationId, effectiveFrom, applicable.Question,
                    remaining, next, actorMemberId, _clock.UtcNow);
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
        var applicable = group is null ? null : ResolveRevision(group, _clock.Today.AddDays(1));
        if (applicable is null || applicable.IsCleared)
        {
            throw new TurnRotationNotFoundException(rotationId);
        }

        var ended = TurnRotationRevision.Cleared(
            Guid.NewGuid(), rotationId, _clock.Today.AddDays(1), applicable.Question,
            actorMemberId, _clock.UtcNow);
        await _repository.AddRevisionAsync(ended, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return await BuildOverviewAsync([.. revisions, ended], cancellationToken);
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
            if (group is null || latest is null || (applicable?.IsCleared ?? false))
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
            _clock.UtcNow);

        await _repository.AddRevisionAsync(revision, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return await BuildOverviewAsync([.. revisions, revision], cancellationToken);
    }

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
        var names = await LoadNamesAsync(revisions, cancellationToken);
        var summaries = new List<TurnRotationSummary>();
        foreach (var group in Groups(revisions))
        {
            var current = ResolveRevision(group, today);
            if (current is { IsCleared: true })
            {
                continue;
            }

            summaries.Add(new TurnRotationSummary(
                group[0].RotationId,
                current is null ? null : MapConfiguration(current),
                Enumerable.Range(0, PreviewDays)
                    .Select(offset => ResolveTurn(group, today.AddDays(offset), names))
                    .OfType<TurnRotationAnswer>()
                    .ToArray()));
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
        IReadOnlyDictionary<Guid, string> names)
    {
        var revision = ResolveRevision(revisions, date);
        var childId = revision?.GetAssignedChildId(date);
        return revision is null || childId is null
            ? null
            : new TurnRotationAnswer(
                revision.RotationId,
                date,
                revision.Question,
                childId.Value,
                names.GetValueOrDefault(childId.Value, "Unknown"));
    }

    private static TurnRotationRevision? ResolveRevision(
        IReadOnlyList<TurnRotationRevision> revisions,
        DateOnly date)
    {
        return revisions
            .Where(revision => revision.EffectiveFrom <= date)
            .OrderByDescending(revision => revision.EffectiveFrom)
            .ThenByDescending(revision => revision.CreatedAtUtc)
            .FirstOrDefault();
    }

    private static TurnRotationConfiguration MapConfiguration(TurnRotationRevision revision)
    {
        return new TurnRotationConfiguration(
            revision.Id,
            revision.EffectiveFrom,
            revision.Question,
            revision.OrderedParticipantChildIds,
            revision.FirstChildId!.Value,
            revision.CreatedByMemberId,
            revision.CreatedAtUtc);
    }
}
