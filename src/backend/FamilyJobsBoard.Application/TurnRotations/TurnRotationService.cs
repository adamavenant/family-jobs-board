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
        return ResolveTurn(revisions, date);
    }

    public async Task<TurnRotationOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var revisions = await _repository.GetRevisionsAsync(cancellationToken);
        return BuildOverview(revisions);
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

        return BuildOverview([.. revisions, revision]);
    }

    private TurnRotationOverview BuildOverview(IReadOnlyList<TurnRotationRevision> revisions)
    {
        var today = _clock.Today;
        var current = ResolveRevision(revisions, today);
        var upcomingTurns = Enumerable.Range(0, PreviewDays)
            .Select(offset => today.AddDays(offset))
            .Select(date => ResolveTurn(revisions, date))
            .Where(answer => answer is not null)
            .Select(answer => answer!)
            .ToArray();

        return new TurnRotationOverview(
            current is null ? null : MapConfiguration(current),
            upcomingTurns);
    }

    private static TurnRotationAnswer? ResolveTurn(
        IReadOnlyList<TurnRotationRevision> revisions,
        DateOnly date)
    {
        var revision = ResolveRevision(revisions, date);
        return revision is null
            ? null
            : new TurnRotationAnswer(date, revision.Question, revision.GetAssignedChildId(date));
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
            revision.FirstChildId,
            revision.CreatedByMemberId,
            revision.CreatedAtUtc);
    }
}
