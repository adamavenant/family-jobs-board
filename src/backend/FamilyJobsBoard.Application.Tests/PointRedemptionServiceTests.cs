using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Application.PointRedemptions;
using FamilyJobsBoard.Application.Points;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.PointRedemptions;
using FamilyJobsBoard.Domain.Points;
using Xunit;

namespace FamilyJobsBoard.Application.Tests;

public sealed class PointRedemptionServiceTests
{
    private static readonly Guid AdultId = Guid.Parse("d55b77d7-a514-4071-a004-f5033aff2f9d");
    private static readonly HouseholdMember Child = new(
        Guid.Parse("96e7d927-dfb6-480c-89be-a5c58144f603"),
        "Fredster",
        "Avenant",
        HouseholdRole.Child);

    private static readonly DateTimeOffset Now = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Redeeming_records_one_negative_ledger_entry_and_returns_the_new_balance()
    {
        var repository = new FakeRepository { Balance = 12 };
        var service = new PointRedemptionService(repository, new FixedClock());

        var result = await service.RedeemAsync(
            Request(points: 5) with { Reward = "  Ice cream  " },
            CancellationToken.None);

        Assert.True(result.WasCreated);
        Assert.Equal(7, result.PointsBalance);
        Assert.Equal(5, result.Redemption.Points);
        Assert.Equal("Ice cream", result.Redemption.Reward);
        Assert.Equal(AdultId, result.Redemption.RedeemedByMemberId);
        Assert.Equal(Now, result.Redemption.RedeemedAtUtc);
        var entry = Assert.Single(repository.Entries);
        Assert.Equal(result.Redemption.Id, entry.PointRedemptionId);
        Assert.Equal(-5, entry.Amount);
        Assert.Equal(Child.Id, entry.ChildId);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Redeeming_exactly_the_balance_leaves_zero()
    {
        var repository = new FakeRepository { Balance = 5 };
        var service = new PointRedemptionService(repository, new FixedClock());

        var result = await service.RedeemAsync(Request(points: 5), CancellationToken.None);

        Assert.Equal(0, result.PointsBalance);
    }

    [Theory]
    [InlineData(6, 5, "Fredster only has 5 points.")]
    [InlineData(2, 1, "Fredster only has 1 point.")]
    [InlineData(1, 0, "Fredster doesn't have any points.")]
    public async Task Redeeming_more_than_the_balance_is_rejected_and_records_nothing(
        int points,
        int balance,
        string message)
    {
        var repository = new FakeRepository { Balance = balance };
        var service = new PointRedemptionService(repository, new FixedClock());

        var exception = await Assert.ThrowsAsync<InsufficientPointsException>(() =>
            service.RedeemAsync(Request(points), CancellationToken.None));

        Assert.Equal(balance, exception.CurrentBalance);
        Assert.Equal(message, exception.Message);
        Assert.Empty(repository.Entries);
        Assert.Equal(0, repository.SaveCount);
        var pointsLock = Assert.Single(repository.Locks);
        Assert.False(pointsLock.Committed);
        Assert.True(pointsLock.Disposed);
    }

    [Fact]
    public async Task Every_redemption_holds_the_childs_lock_and_commits_once_recorded()
    {
        var repository = new FakeRepository { Balance = 9 };
        var service = new PointRedemptionService(repository, new FixedClock());

        await service.RedeemAsync(Request(points: 9), CancellationToken.None);

        var pointsLock = Assert.Single(repository.Locks);
        Assert.Equal(Child.Id, pointsLock.ChildId);
        Assert.True(pointsLock.SavedWhileHeld);
        Assert.True(pointsLock.Committed);
        Assert.True(pointsLock.Disposed);
    }

    [Theory]
    [InlineData(0, "Ice cream", "Points")]
    [InlineData(-3, "Ice cream", "Points")]
    [InlineData(5, null, "Reward")]
    [InlineData(5, "   ", "Reward")]
    public async Task Invalid_details_are_reported_by_field(int points, string? reward, string field)
    {
        var repository = new FakeRepository { Balance = 50 };
        var service = new PointRedemptionService(repository, new FixedClock());

        var exception = await Assert.ThrowsAsync<InvalidPointRedemptionException>(() =>
            service.RedeemAsync(Request(points) with { Reward = reward }, CancellationToken.None));

        Assert.Contains(field, exception.Errors.Keys);
        Assert.Empty(repository.Entries);
    }

    [Fact]
    public async Task Over_long_reward_and_unknown_child_and_missing_request_id_are_rejected()
    {
        var repository = new FakeRepository { Balance = 50 };
        var service = new PointRedemptionService(repository, new FixedClock());

        var longReward = await Assert.ThrowsAsync<InvalidPointRedemptionException>(() =>
            service.RedeemAsync(
                Request(1) with { Reward = new string('a', PointRedemption.MaximumRewardLength + 1) },
                CancellationToken.None));
        var unknownChild = await Assert.ThrowsAsync<InvalidPointRedemptionException>(() =>
            service.RedeemAsync(Request(1) with { ChildId = Guid.NewGuid() }, CancellationToken.None));
        var noRequest = await Assert.ThrowsAsync<InvalidPointRedemptionException>(() =>
            service.RedeemAsync(Request(1) with { RequestId = Guid.Empty }, CancellationToken.None));

        Assert.Contains("Reward", longReward.Errors.Keys);
        Assert.Contains("ChildId", unknownChild.Errors.Keys);
        Assert.Contains("RequestId", noRequest.Errors.Keys);
        Assert.Empty(repository.Entries);
    }

    [Fact]
    public async Task Retrying_the_same_request_records_one_entry()
    {
        var repository = new FakeRepository { Balance = 5 };
        var service = new PointRedemptionService(repository, new FixedClock());
        var request = Request(points: 5);
        var first = await service.RedeemAsync(request, CancellationToken.None);

        var retry = await service.RedeemAsync(
            request with { Reward = " Ice cream " },
            CancellationToken.None);

        Assert.False(retry.WasCreated);
        Assert.Equal(first.Redemption.Id, retry.Redemption.Id);
        Assert.Equal(0, retry.PointsBalance);
        Assert.Single(repository.Entries);
    }

    [Fact]
    public async Task A_retry_recorded_while_waiting_for_the_lock_is_replayed_not_rechecked()
    {
        var repository = new FakeRepository { Balance = 5 };
        var service = new PointRedemptionService(repository, new FixedClock());
        var request = Request(points: 5);
        repository.OnLockAcquired = () =>
        {
            // The original attempt spent the points while this retry waited for the lock.
            var original = new PointRedemption(
                Guid.NewGuid(), request.RequestId, Child.Id, AdultId, 5, "Ice cream", Now);
            repository.Redemptions.Add(original);
            repository.Entries.Add(PointsLedgerEntry.ForRedemption(
                Guid.NewGuid(), Child.Id, original.Id, 5, Now));
        };

        var result = await service.RedeemAsync(request, CancellationToken.None);

        Assert.False(result.WasCreated);
        Assert.Equal(0, result.PointsBalance);
        Assert.Single(repository.Entries);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task Reusing_a_request_id_for_different_details_is_a_conflict()
    {
        var repository = new FakeRepository { Balance = 20 };
        var service = new PointRedemptionService(repository, new FixedClock());
        var request = Request(points: 3);
        await service.RedeemAsync(request, CancellationToken.None);

        await Assert.ThrowsAsync<PointRedemptionRequestConflictException>(() =>
            service.RedeemAsync(request with { Points = 4 }, CancellationToken.None));
        await Assert.ThrowsAsync<PointRedemptionRequestConflictException>(() =>
            service.RedeemAsync(request with { Reward = "Movie night" }, CancellationToken.None));
        await Assert.ThrowsAsync<PointRedemptionRequestConflictException>(() =>
            service.RedeemAsync(
                request with { RedeemedByMemberId = Guid.NewGuid() },
                CancellationToken.None));
        await Assert.ThrowsAsync<PointRedemptionRequestConflictException>(() =>
            service.RedeemAsync(request with { ChildId = Guid.NewGuid() }, CancellationToken.None));
        Assert.Single(repository.Entries);
    }

    [Fact]
    public async Task A_request_id_taken_concurrently_for_another_child_is_a_conflict()
    {
        var repository = new FakeRepository { Balance = 5 };
        var service = new PointRedemptionService(repository, new FixedClock());
        var request = Request(points: 2);
        repository.SimulateConcurrentWinner = () =>
        {
            var winner = new PointRedemption(
                Guid.NewGuid(), request.RequestId, Guid.NewGuid(), AdultId, 2, "Ice cream", Now);
            repository.Redemptions.Add(winner);
        };

        await Assert.ThrowsAsync<PointRedemptionRequestConflictException>(() =>
            service.RedeemAsync(request, CancellationToken.None));

        Assert.Empty(repository.Entries);
        var pointsLock = Assert.Single(repository.Locks);
        Assert.False(pointsLock.Committed);
        Assert.True(pointsLock.Disposed);
    }

    private static RedeemPoints Request(int points) =>
        new(Guid.NewGuid(), AdultId, Child.Id, points, "Ice cream");

    private sealed class FixedClock : IHouseholdClock
    {
        public DateOnly Today => new(2026, 10, 5);

        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeRepository : IPointRedemptionRepository
    {
        private (PointRedemption Redemption, PointsLedgerEntry Entry)? _pending;

        public int Balance { get; set; }

        public List<PointRedemption> Redemptions { get; } = [];

        public List<PointsLedgerEntry> Entries { get; } = [];

        public int SaveCount { get; private set; }

        public Action? SimulateConcurrentWinner { get; set; }

        public Action? OnLockAcquired { get; set; }

        public List<FakeChildPointsLock> Locks { get; } = [];

        public Task<IChildPointsLock> LockChildPointsAsync(
            Guid childId,
            CancellationToken cancellationToken)
        {
            var pointsLock = new FakeChildPointsLock(childId);
            Locks.Add(pointsLock);
            OnLockAcquired?.Invoke();
            return Task.FromResult<IChildPointsLock>(pointsLock);
        }

        public Task<HouseholdMember?> GetActiveChildAsync(
            Guid childId,
            CancellationToken cancellationToken) =>
            Task.FromResult<HouseholdMember?>(childId == Child.Id ? Child : null);

        public Task<PointRedemption?> GetRedemptionByRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Redemptions.SingleOrDefault(item => item.RequestId == requestId));

        public Task AddRedemptionAsync(
            PointRedemption redemption,
            PointsLedgerEntry entry,
            CancellationToken cancellationToken)
        {
            _pending = (redemption, entry);
            return Task.CompletedTask;
        }

        public Task<int> GetPointsBalanceAsync(Guid childId, CancellationToken cancellationToken) =>
            Task.FromResult(Balance + Entries.Where(entry => entry.ChildId == childId).Sum(entry => entry.Amount));

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (Locks.LastOrDefault() is { Committed: false, Disposed: false } held)
            {
                held.SavedWhileHeld = true;
            }

            if (_pending is { } pending)
            {
                _pending = null;
                if (SimulateConcurrentWinner is { } winner)
                {
                    SimulateConcurrentWinner = null;
                    winner();
                    throw new DuplicatePointRedemptionRequestException();
                }

                Redemptions.Add(pending.Redemption);
                Entries.Add(pending.Entry);
            }

            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
