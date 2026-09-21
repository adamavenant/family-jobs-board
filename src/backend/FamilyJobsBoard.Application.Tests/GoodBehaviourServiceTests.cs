using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Application.GoodBehaviours;
using FamilyJobsBoard.Domain.GoodBehaviours;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Points;
using Xunit;

namespace FamilyJobsBoard.Application.Tests;

public sealed class GoodBehaviourServiceTests
{
    private static readonly Guid AdultId = Guid.Parse("d55b77d7-a514-4071-a004-f5033aff2f9d");
    private static readonly HouseholdMember Child = new(
        Guid.Parse("96e7d927-dfb6-480c-89be-a5c58144f603"),
        "Fredster",
        "Avenant",
        HouseholdRole.Child);
    private static readonly HouseholdMember SecondChild = new(
        Guid.Parse("3fdb1469-df09-420e-8eeb-330717c710fe"),
        "Harrie",
        "Avenant",
        HouseholdRole.Child);

    [Fact]
    public async Task Creating_a_type_makes_it_available_and_records_the_adult()
    {
        var repository = new FakeRepository();
        var service = new GoodBehaviourService(repository, new FixedClock());

        var created = await service.CreateTypeAsync(
            AdultId,
            new SaveGoodBehaviourType("  Being Helpful ", null, 5),
            CancellationToken.None);

        var listed = await service.ListTypesAsync(CancellationToken.None);
        Assert.Equal(created.Id, Assert.Single(listed).Id);
        Assert.Equal("Being Helpful", created.Name);
        Assert.Equal(AdultId, repository.Types.Single().CreatedByMemberId);
    }

    [Theory]
    [InlineData(null, 5, "Name")]
    [InlineData("  ", 5, "Name")]
    [InlineData("Ok", -1, "Points")]
    public async Task Invalid_type_data_is_reported_by_field(string? name, int points, string field)
    {
        var service = new GoodBehaviourService(new FakeRepository(), new FixedClock());

        var exception = await Assert.ThrowsAsync<InvalidGoodBehaviourException>(() =>
            service.CreateTypeAsync(
                AdultId,
                new SaveGoodBehaviourType(name, null, points),
                CancellationToken.None));

        Assert.Contains(field, exception.Errors.Keys);
    }

    [Fact]
    public async Task Deleted_types_are_no_longer_listed_and_delete_can_be_repeated()
    {
        var repository = new FakeRepository();
        var type = repository.AddType();
        var service = new GoodBehaviourService(repository, new FixedClock());

        await service.DeleteTypeAsync(AdultId, type.Id, CancellationToken.None);
        await service.DeleteTypeAsync(AdultId, type.Id, CancellationToken.None);

        Assert.Empty(await service.ListTypesAsync(CancellationToken.None));
        await Assert.ThrowsAsync<GoodBehaviourTypeNotFoundException>(() =>
            service.DeleteTypeAsync(AdultId, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Logging_awards_the_types_points_by_default_in_one_save()
    {
        var repository = new FakeRepository();
        var type = repository.AddType(points: 10);
        var service = new GoodBehaviourService(repository, new FixedClock());

        var result = await service.LogAsync(
            Request(type.Id, points: null),
            CancellationToken.None);

        Assert.True(result.WasCreated);
        var logged = Assert.Single(result.Awards);
        Assert.Equal(10, logged.Behaviour.Points);
        Assert.Equal(10, logged.PointsBalance);
        var award = Assert.Single(repository.Awards);
        Assert.Equal(logged.Behaviour.Id, award.GoodBehaviourId);
        Assert.Equal(10, award.Amount);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Logging_allows_the_point_value_to_be_overridden()
    {
        var repository = new FakeRepository();
        var type = repository.AddType(points: 10);
        var service = new GoodBehaviourService(repository, new FixedClock());

        var result = await service.LogAsync(
            Request(type.Id, points: 3),
            CancellationToken.None);

        Assert.Equal(3, Assert.Single(result.Awards).Behaviour.Points);
        Assert.Equal(3, Assert.Single(repository.Awards).Amount);
    }

    [Fact]
    public async Task Retrying_the_same_request_awards_points_once()
    {
        var repository = new FakeRepository();
        var type = repository.AddType(points: 10);
        var service = new GoodBehaviourService(repository, new FixedClock());
        var request = Request(type.Id, points: null);

        var first = await service.LogAsync(request, CancellationToken.None);
        var retry = await service.LogAsync(request, CancellationToken.None);

        Assert.True(first.WasCreated);
        Assert.False(retry.WasCreated);
        Assert.Equal(first.Awards[0].Behaviour.Id, retry.Awards[0].Behaviour.Id);
        Assert.Single(repository.Awards);
        Assert.Equal(10, retry.Awards[0].PointsBalance);
    }

    [Fact]
    public async Task Retry_after_the_type_is_deleted_or_edited_still_returns_the_original()
    {
        var repository = new FakeRepository();
        var type = repository.AddType(points: 10);
        var service = new GoodBehaviourService(repository, new FixedClock());
        var request = Request(type.Id, points: null);
        var first = await service.LogAsync(request, CancellationToken.None);

        await service.UpdateTypeAsync(
            AdultId,
            type.Id,
            new SaveGoodBehaviourType("Renamed", null, 99),
            CancellationToken.None);
        await service.DeleteTypeAsync(AdultId, type.Id, CancellationToken.None);
        var retry = await service.LogAsync(request, CancellationToken.None);

        Assert.Equal(first.Awards[0].Behaviour.Id, retry.Awards[0].Behaviour.Id);
        Assert.Equal("Being Brave", retry.Awards[0].Behaviour.TypeName);
        Assert.Equal(10, retry.Awards[0].Behaviour.Points);
        Assert.Single(repository.Awards);
    }

    [Fact]
    public async Task Reusing_a_request_id_for_different_details_is_a_conflict()
    {
        var repository = new FakeRepository();
        var type = repository.AddType(points: 10);
        var service = new GoodBehaviourService(repository, new FixedClock());
        var request = Request(type.Id, points: 5);
        await service.LogAsync(request, CancellationToken.None);

        await Assert.ThrowsAsync<GoodBehaviourRequestConflictException>(() =>
            service.LogAsync(request with { Points = 6 }, CancellationToken.None));
        await Assert.ThrowsAsync<GoodBehaviourRequestConflictException>(() =>
            service.LogAsync(request with { LoggedByMemberId = Guid.NewGuid() }, CancellationToken.None));
        await Assert.ThrowsAsync<GoodBehaviourRequestConflictException>(() =>
            service.LogAsync(
                request with { ChildIds = [Child.Id, SecondChild.Id] },
                CancellationToken.None));
        Assert.Single(repository.Awards);
    }

    [Fact]
    public async Task Concurrent_duplicate_request_resolves_to_the_winning_behaviour()
    {
        var repository = new FakeRepository();
        var type = repository.AddType(points: 10);
        var service = new GoodBehaviourService(repository, new FixedClock());
        var request = Request(type.Id, points: null);
        repository.SimulateConcurrentWinner = () =>
        {
            var winner = new GoodBehaviour(
                Guid.NewGuid(), request.RequestId, type, Child.Id, AdultId, 10, DateTimeOffset.UtcNow);
            repository.Behaviours.Add(winner);
            repository.Awards.Add(PointsLedgerEntry.ForGoodBehaviour(
                Guid.NewGuid(), Child.Id, winner.Id, 10, DateTimeOffset.UtcNow));
        };

        var result = await service.LogAsync(request, CancellationToken.None);

        Assert.False(result.WasCreated);
        Assert.Single(repository.Awards);
    }

    [Fact]
    public async Task Logging_rejects_unknown_or_deleted_types_and_inactive_children()
    {
        var repository = new FakeRepository();
        var deleted = repository.AddType();
        deleted.Deactivate(AdultId, DateTimeOffset.UtcNow);
        var service = new GoodBehaviourService(repository, new FixedClock());

        var missingType = await Assert.ThrowsAsync<InvalidGoodBehaviourException>(() =>
            service.LogAsync(Request(Guid.NewGuid(), null), CancellationToken.None));
        var deletedType = await Assert.ThrowsAsync<InvalidGoodBehaviourException>(() =>
            service.LogAsync(Request(deleted.Id, null), CancellationToken.None));
        var noChild = await Assert.ThrowsAsync<InvalidGoodBehaviourException>(() =>
            service.LogAsync(
                Request(deleted.Id, null) with { ChildIds = [Guid.NewGuid()] },
                CancellationToken.None));

        Assert.Contains("TypeId", missingType.Errors.Keys);
        Assert.Contains("TypeId", deletedType.Errors.Keys);
        Assert.Contains("ChildIds", noChild.Errors.Keys);
        Assert.Empty(repository.Awards);
    }

    [Fact]
    public async Task Logging_for_both_children_creates_an_independent_award_for_each()
    {
        var repository = new FakeRepository();
        var type = repository.AddType(points: 10);
        var service = new GoodBehaviourService(repository, new FixedClock());

        var result = await service.LogAsync(
            Request(type.Id, points: 4) with { ChildIds = [Child.Id, SecondChild.Id] },
            CancellationToken.None);

        Assert.True(result.WasCreated);
        Assert.Equal(2, result.Awards.Count);
        Assert.Equal(2, result.Awards.Select(item => item.Behaviour.Id).Distinct().Count());
        Assert.Equal(
            new[] { Child.Id, SecondChild.Id }.Order(),
            result.Awards.Select(item => item.Behaviour.ChildId).Order());
        Assert.All(result.Awards, item =>
        {
            Assert.Equal(4, item.Behaviour.Points);
            Assert.Equal(4, item.PointsBalance);
        });
        Assert.Equal(2, repository.Awards.Count);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Retrying_a_multi_child_request_awards_each_child_once_in_any_order()
    {
        var repository = new FakeRepository();
        var type = repository.AddType(points: 10);
        var service = new GoodBehaviourService(repository, new FixedClock());
        var request = Request(type.Id, null) with { ChildIds = [Child.Id, SecondChild.Id] };
        await service.LogAsync(request, CancellationToken.None);

        var retry = await service.LogAsync(
            request with { ChildIds = [SecondChild.Id, Child.Id] },
            CancellationToken.None);

        Assert.False(retry.WasCreated);
        Assert.Equal(2, retry.Awards.Count);
        Assert.Equal(2, repository.Awards.Count);
    }

    [Fact]
    public async Task One_invalid_child_rejects_the_whole_request_without_any_award()
    {
        var repository = new FakeRepository();
        var type = repository.AddType();
        var service = new GoodBehaviourService(repository, new FixedClock());

        var exception = await Assert.ThrowsAsync<InvalidGoodBehaviourException>(() =>
            service.LogAsync(
                Request(type.Id, null) with { ChildIds = [Child.Id, Guid.NewGuid()] },
                CancellationToken.None));

        Assert.Contains("ChildIds", exception.Errors.Keys);
        Assert.Empty(repository.Awards);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Logging_needs_one_or_more_distinct_children(int duplicates)
    {
        var repository = new FakeRepository();
        var type = repository.AddType();
        var service = new GoodBehaviourService(repository, new FixedClock());
        IReadOnlyList<Guid> childIds = duplicates == 0 ? [] : [Child.Id, Child.Id];

        var exception = await Assert.ThrowsAsync<InvalidGoodBehaviourException>(() =>
            service.LogAsync(
                Request(type.Id, null) with { ChildIds = childIds },
                CancellationToken.None));

        Assert.Contains("ChildIds", exception.Errors.Keys);
        Assert.Empty(repository.Awards);
    }

    [Fact]
    public async Task Logging_rejects_negative_points_and_a_missing_request_id()
    {
        var repository = new FakeRepository();
        var type = repository.AddType();
        var service = new GoodBehaviourService(repository, new FixedClock());

        var negative = await Assert.ThrowsAsync<InvalidGoodBehaviourException>(() =>
            service.LogAsync(Request(type.Id, -1), CancellationToken.None));
        var noRequest = await Assert.ThrowsAsync<InvalidGoodBehaviourException>(() =>
            service.LogAsync(
                Request(type.Id, null) with { RequestId = Guid.Empty },
                CancellationToken.None));

        Assert.Contains("Points", negative.Errors.Keys);
        Assert.Contains("RequestId", noRequest.Errors.Keys);
    }

    private static LogGoodBehaviour Request(Guid typeId, int? points) =>
        new(Guid.NewGuid(), AdultId, typeId, [Child.Id], points);

    private sealed class FixedClock : IHouseholdClock
    {
        public DateOnly Today => new(2026, 9, 21);

        public DateTimeOffset UtcNow => new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeRepository : IGoodBehaviourRepository
    {
        public List<GoodBehaviourType> Types { get; } = [];

        public List<GoodBehaviour> Behaviours { get; } = [];

        public List<PointsLedgerEntry> Awards { get; } = [];

        public int SaveCount { get; private set; }

        public Action? SimulateConcurrentWinner { get; set; }

        public GoodBehaviourType AddType(int points = 10)
        {
            var type = new GoodBehaviourType(
                Guid.NewGuid(), "Being Brave", "Tried something hard.", points, AdultId, DateTimeOffset.UtcNow);
            Types.Add(type);
            return type;
        }

        public Task<IReadOnlyList<GoodBehaviourType>> GetActiveTypesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GoodBehaviourType>>(
                Types.Where(type => type.IsActive).ToArray());

        public Task<GoodBehaviourType?> GetTypeAsync(Guid typeId, CancellationToken cancellationToken) =>
            Task.FromResult(Types.SingleOrDefault(type => type.Id == typeId));

        public Task AddTypeAsync(GoodBehaviourType type, CancellationToken cancellationToken)
        {
            Types.Add(type);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<HouseholdMember>> GetActiveChildrenAsync(
            IReadOnlyCollection<Guid> childIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HouseholdMember>>(
                new[] { Child, SecondChild }.Where(child => childIds.Contains(child.Id)).ToArray());

        public Task<IReadOnlyList<GoodBehaviour>> GetBehavioursByRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GoodBehaviour>>(
                Behaviours.Where(item => item.RequestId == requestId).ToArray());

        public Task AddBehavioursAsync(
            IReadOnlyCollection<(GoodBehaviour Behaviour, PointsLedgerEntry Award)> entries,
            CancellationToken cancellationToken)
        {
            _pending = entries.ToArray();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<Guid, int>> GetPointsBalancesAsync(
            IReadOnlyCollection<Guid> childIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, int>>(
                Awards.Where(entry => childIds.Contains(entry.ChildId))
                    .GroupBy(entry => entry.ChildId)
                    .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Amount)));

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (_pending is { } pending)
            {
                _pending = null;
                if (SimulateConcurrentWinner is { } winner)
                {
                    SimulateConcurrentWinner = null;
                    winner();
                    throw new DuplicateGoodBehaviourRequestException();
                }

                Behaviours.AddRange(pending.Select(entry => entry.Behaviour));
                Awards.AddRange(pending.Select(entry => entry.Award));
            }

            SaveCount++;
            return Task.CompletedTask;
        }

        private (GoodBehaviour Behaviour, PointsLedgerEntry Award)[]? _pending;
    }
}
