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

    public async Task<TurnRotationAnswer?> GetTurnAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var revisions = await _repository.GetRevisionsAsync(cancellationToken);
        var names = await LoadNamesAsync(revisions, cancellationToken);
        return ResolveTurn(revisions, date, names);
    }

    public async Task<TurnRotationOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var revisions = await _repository.GetRevisionsAsync(cancellationToken);
        return await BuildOverviewAsync(revisions, cancellationToken);
    }

    /// <summary>
    /// Creates a replacement revision effective tomorrow that leaves out a deactivated
    /// child, keeping the next assignee unchanged where possible. Earlier dates keep
    /// their answers, and restoring the child does not re-add them.
    /// </summary>
    public async Task RemoveParticipantAsync(
        Guid childId,
        Guid actorMemberId,
        CancellationToken cancellationToken)
    {
        var revisions = await _repository.GetRevisionsAsync(cancellationToken);
        var effectiveFrom = _clock.Today.AddDays(1);
        var applicable = ResolveRevision(revisions, effectiveFrom);
        if (applicable is null || !applicable.OrderedParticipantChildIds.Contains(childId))
        {
            return;
        }

        var order = applicable.OrderedParticipantChildIds;
        var remaining = order.Where(id => id != childId).ToArray();
        TurnRotationRevision replacement;
        if (remaining.Length == 0)
        {
            replacement = TurnRotationRevision.Cleared(
                Guid.NewGuid(), effectiveFrom, applicable.Question, actorMemberId, _clock.UtcNow);
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
                Guid.NewGuid(), effectiveFrom, applicable.Question, remaining, next,
                actorMemberId, _clock.UtcNow);
        }

        await _repository.AddRevisionAsync(replacement, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    public async Task<TurnRotationOverview> SaveAsync(
        Guid actorMemberId,
        SaveTurnRotation request,
        CancellationToken cancellationToken)
    {
        var actor = await _repository.GetMemberAsync(actorMemberId, cancellationToken);
        if (actor is not { IsAdult: true })
        {
            throw new TurnRotationForbiddenException();
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

        var revisions = await _repository.GetRevisionsAsync(cancellationToken);
        var minimumEffectiveFrom = revisions.Count == 0
            ? _clock.Today
            : _clock.Today.AddDays(1);
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

    private async Task<TurnRotationOverview> BuildOverviewAsync(
        IReadOnlyList<TurnRotationRevision> revisions,
        CancellationToken cancellationToken)
    {
        var today = _clock.Today;
        var names = await LoadNamesAsync(revisions, cancellationToken);
        var current = ResolveRevision(revisions, today);
        var upcomingTurns = Enumerable.Range(0, PreviewDays)
            .Select(offset => ResolveTurn(revisions, today.AddDays(offset), names))
            .OfType<TurnRotationAnswer>()
            .ToArray();

        return new TurnRotationOverview(
            current is null || current.IsCleared ? null : MapConfiguration(current),
            upcomingTurns,
            revisions.Count > 0);
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
