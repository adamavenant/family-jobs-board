using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Domain.GoodBehaviours;
using FamilyJobsBoard.Domain.Points;

namespace FamilyJobsBoard.Application.GoodBehaviours;

public sealed class GoodBehaviourService
{
    private readonly IGoodBehaviourRepository _repository;
    private readonly IHouseholdClock _clock;

    public GoodBehaviourService(IGoodBehaviourRepository repository, IHouseholdClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<IReadOnlyList<GoodBehaviourTypeSummary>> ListTypesAsync(
        CancellationToken cancellationToken)
    {
        var types = await _repository.GetActiveTypesAsync(cancellationToken);
        return types.Select(MapType).ToArray();
    }

    public async Task<GoodBehaviourTypeSummary> CreateTypeAsync(
        Guid actorMemberId,
        SaveGoodBehaviourType request,
        CancellationToken cancellationToken)
    {
        ValidateType(request);
        var type = new GoodBehaviourType(
            Guid.NewGuid(),
            request.Name!,
            request.Description ?? string.Empty,
            request.Points,
            actorMemberId,
            _clock.UtcNow);

        await _repository.AddTypeAsync(type, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return MapType(type);
    }

    public async Task<GoodBehaviourTypeSummary> UpdateTypeAsync(
        Guid actorMemberId,
        Guid typeId,
        SaveGoodBehaviourType request,
        CancellationToken cancellationToken)
    {
        ValidateType(request);
        var type = await GetTypeAsync(typeId, cancellationToken);
        type.Update(
            request.Name!,
            request.Description ?? string.Empty,
            request.Points,
            actorMemberId,
            _clock.UtcNow);

        await _repository.SaveChangesAsync(cancellationToken);
        return MapType(type);
    }

    public async Task DeleteTypeAsync(
        Guid actorMemberId,
        Guid typeId,
        CancellationToken cancellationToken)
    {
        var type = await GetTypeAsync(typeId, cancellationToken);
        type.Deactivate(actorMemberId, _clock.UtcNow);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    public async Task<GoodBehaviourLogResult> LogAsync(
        LogGoodBehaviour request,
        CancellationToken cancellationToken)
    {
        if (request.RequestId == Guid.Empty)
        {
            throw new InvalidGoodBehaviourException(new Dictionary<string, string[]>
            {
                [nameof(LogGoodBehaviour.RequestId)] = ["A request ID is required."],
            });
        }

        var existing = await _repository.GetBehaviourByRequestAsync(
            request.RequestId,
            cancellationToken);
        if (existing is not null)
        {
            return await ReplayAsync(existing, request, cancellationToken);
        }

        var errors = new Dictionary<string, string[]>();
        if (request.Points is < 0)
        {
            errors[nameof(LogGoodBehaviour.Points)] = ["Points cannot be negative."];
        }

        var type = await _repository.GetTypeAsync(request.TypeId, cancellationToken);
        if (type is not { IsActive: true })
        {
            errors[nameof(LogGoodBehaviour.TypeId)] = ["Choose an available good behaviour."];
        }

        var child = await _repository.GetActiveChildAsync(request.ChildId, cancellationToken);
        if (child is null)
        {
            errors[nameof(LogGoodBehaviour.ChildId)] = ["Choose an active child in this household."];
        }

        if (errors.Count > 0)
        {
            throw new InvalidGoodBehaviourException(errors);
        }

        var loggedAtUtc = _clock.UtcNow;
        var points = request.Points ?? type!.Points;
        var behaviour = new GoodBehaviour(
            Guid.NewGuid(),
            request.RequestId,
            type!,
            child!.Id,
            request.LoggedByMemberId,
            points,
            loggedAtUtc);
        var award = PointsLedgerEntry.ForGoodBehaviour(
            Guid.NewGuid(),
            child.Id,
            behaviour.Id,
            points,
            loggedAtUtc);

        await _repository.AddBehaviourAsync(behaviour, award, cancellationToken);
        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateGoodBehaviourRequestException)
        {
            var winner = await _repository.GetBehaviourByRequestAsync(
                request.RequestId,
                cancellationToken)
                ?? throw new GoodBehaviourRequestConflictException(request.RequestId);
            return await ReplayAsync(winner, request, cancellationToken);
        }

        return new GoodBehaviourLogResult(
            MapBehaviour(behaviour),
            await _repository.GetPointsBalanceAsync(child.Id, cancellationToken),
            WasCreated: true);
    }

    private async Task<GoodBehaviourLogResult> ReplayAsync(
        GoodBehaviour existing,
        LogGoodBehaviour request,
        CancellationToken cancellationToken)
    {
        // A retry repeats the original payload; an omitted amount means "the type's default
        // at the time", which is whatever the original request resolved to.
        var samePayload = existing.TypeId == request.TypeId
            && existing.ChildId == request.ChildId
            && existing.LoggedByMemberId == request.LoggedByMemberId
            && (request.Points is null || existing.Points == request.Points);
        if (!samePayload)
        {
            throw new GoodBehaviourRequestConflictException(request.RequestId);
        }

        return new GoodBehaviourLogResult(
            MapBehaviour(existing),
            await _repository.GetPointsBalanceAsync(existing.ChildId, cancellationToken),
            WasCreated: false);
    }

    private async Task<GoodBehaviourType> GetTypeAsync(
        Guid typeId,
        CancellationToken cancellationToken)
    {
        return await _repository.GetTypeAsync(typeId, cancellationToken)
            ?? throw new GoodBehaviourTypeNotFoundException(typeId);
    }

    private static void ValidateType(SaveGoodBehaviourType request)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            errors[nameof(SaveGoodBehaviourType.Name)] = ["Enter a name."];
        }
        else if (name.Length > GoodBehaviourType.MaximumNameLength)
        {
            errors[nameof(SaveGoodBehaviourType.Name)] =
                [$"The name must be {GoodBehaviourType.MaximumNameLength} characters or fewer."];
        }

        if (request.Description?.Trim().Length > GoodBehaviourType.MaximumDescriptionLength)
        {
            errors[nameof(SaveGoodBehaviourType.Description)] =
                [$"The description must be {GoodBehaviourType.MaximumDescriptionLength} characters or fewer."];
        }

        if (request.Points < 0)
        {
            errors[nameof(SaveGoodBehaviourType.Points)] = ["Points cannot be negative."];
        }

        if (errors.Count > 0)
        {
            throw new InvalidGoodBehaviourException(errors);
        }
    }

    private static GoodBehaviourTypeSummary MapType(GoodBehaviourType type) =>
        new(type.Id, type.Name, type.Description, type.Points);

    private static LoggedGoodBehaviour MapBehaviour(GoodBehaviour behaviour) =>
        new(
            behaviour.Id,
            behaviour.TypeId,
            behaviour.TypeName,
            behaviour.TypeDescription,
            behaviour.ChildId,
            behaviour.LoggedByMemberId,
            behaviour.Points,
            behaviour.LoggedAtUtc);
}
