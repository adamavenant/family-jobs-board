using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Application.PointAdjustments;
using FamilyJobsBoard.Application.Points;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.PointAdjustments;
using FamilyJobsBoard.Domain.Points;
using Xunit;

namespace FamilyJobsBoard.Application.Tests;

public sealed class PointAdjustmentServiceTests
{
    private static readonly Guid AdultId = Guid.Parse("d55b77d7-a514-4071-a004-f5033aff2f9d");
    private static readonly HouseholdMember Child = new(
        Guid.Parse("96e7d927-dfb6-480c-89be-a5c58144f603"),
        "Fredster",
        "Avenant",
        HouseholdRole.Child);

    [Fact]
    public async Task Adding_points_records_one_ledger_entry_and_returns_the_new_balance()
    {
        var repository = new FakeRepository { Balance = 4 };
        var service = new PointAdjustmentService(repository, new FixedClock());

        var result = await service.RecordAsync(Request(amount: 6), CancellationToken.None);

        Assert.True(result.WasCreated);
        Assert.Equal(10, result.PointsBalance);
        Assert.Equal(6, result.Adjustment.Amount);
        Assert.Equal("Helped a neighbour", result.Adjustment.Reason);
        var entry = Assert.Single(repository.Entries);
        Assert.Equal(result.Adjustment.Id, entry.PointAdjustmentId);
        Assert.Equal(6, entry.Amount);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Removing_points_down_to_zero_is_allowed()
    {
        var repository = new FakeRepository { Balance = 10 };
        var service = new PointAdjustmentService(repository, new FixedClock());

        var result = await service.RecordAsync(Request(amount: -10), CancellationToken.None);

        Assert.Equal(0, result.PointsBalance);
        Assert.Equal(-10, Assert.Single(repository.Entries).Amount);
    }

    [Fact]
    public async Task Removing_more_than_the_balance_is_rejected_and_records_nothing()
    {
        var repository = new FakeRepository { Balance = 3 };
        var service = new PointAdjustmentService(repository, new FixedClock());

        var exception = await Assert.ThrowsAsync<InsufficientPointsException>(() =>
            service.RecordAsync(Request(amount: -5), CancellationToken.None));

        Assert.Equal(3, exception.CurrentBalance);
        Assert.Equal("Fredster only has 3 points.", exception.Message);
        Assert.Empty(repository.Entries);
        Assert.Equal(0, repository.SaveCount);
        var pointsLock = Assert.Single(repository.Locks);
        Assert.False(pointsLock.Committed);
        Assert.True(pointsLock.Disposed);
    }

    [Fact]
    public async Task Removing_points_from_a_balance_that_was_already_negative_is_rejected()
    {
        var repository = new FakeRepository { Balance = -8 };
        var service = new PointAdjustmentService(repository, new FixedClock());

        var exception = await Assert.ThrowsAsync<InsufficientPointsException>(() =>
            service.RecordAsync(Request(amount: -1), CancellationToken.None));

        Assert.Equal(-8, exception.CurrentBalance);
        Assert.Equal("Fredster doesn't have any points.", exception.Message);
        Assert.Empty(repository.Entries);
    }

    [Fact]
    public async Task Every_adjustment_holds_the_childs_lock_and_commits_once_recorded()
    {
        var repository = new FakeRepository { Balance = 4 };
        var service = new PointAdjustmentService(repository, new FixedClock());

        await service.RecordAsync(Request(amount: -4), CancellationToken.None);

        var pointsLock = Assert.Single(repository.Locks);
        Assert.Equal(Child.Id, pointsLock.ChildId);
        Assert.True(pointsLock.SavedWhileHeld);
        Assert.True(pointsLock.Committed);
        Assert.True(pointsLock.Disposed);
    }

    [Fact]
    public async Task Adding_points_to_an_already_negative_balance_is_allowed()
    {
        var repository = new FakeRepository { Balance = -8 };
        var service = new PointAdjustmentService(repository, new FixedClock());

        var result = await service.RecordAsync(Request(amount: 2), CancellationToken.None);

        Assert.Equal(-6, result.PointsBalance);
    }

    [Theory]
    [InlineData(0, "Reason", "Amount")]
    [InlineData(5, null, "Reason")]
    [InlineData(5, "   ", "Reason")]
    public async Task Invalid_details_are_reported_by_field(int amount, string? reason, string field)
    {
        var repository = new FakeRepository();
        var service = new PointAdjustmentService(repository, new FixedClock());

        var exception = await Assert.ThrowsAsync<InvalidPointAdjustmentException>(() =>
            service.RecordAsync(Request(amount) with { Reason = reason }, CancellationToken.None));

        Assert.Contains(field, exception.Errors.Keys);
        Assert.Empty(repository.Entries);
    }

    [Fact]
    public async Task Over_long_reason_and_unknown_child_and_missing_request_id_are_rejected()
    {
        var repository = new FakeRepository();
        var service = new PointAdjustmentService(repository, new FixedClock());

        var longReason = await Assert.ThrowsAsync<InvalidPointAdjustmentException>(() =>
            service.RecordAsync(
                Request(1) with { Reason = new string('a', PointAdjustment.MaximumReasonLength + 1) },
                CancellationToken.None));
        var unknownChild = await Assert.ThrowsAsync<InvalidPointAdjustmentException>(() =>
            service.RecordAsync(Request(1) with { ChildId = Guid.NewGuid() }, CancellationToken.None));
        var noRequest = await Assert.ThrowsAsync<InvalidPointAdjustmentException>(() =>
            service.RecordAsync(Request(1) with { RequestId = Guid.Empty }, CancellationToken.None));

        Assert.Contains("Reason", longReason.Errors.Keys);
        Assert.Contains("ChildId", unknownChild.Errors.Keys);
        Assert.Contains("RequestId", noRequest.Errors.Keys);
        Assert.Empty(repository.Entries);
    }

    [Fact]
    public async Task Retrying_the_same_request_records_one_entry()
    {
        var repository = new FakeRepository { Balance = 5 };
        var service = new PointAdjustmentService(repository, new FixedClock());
        var request = Request(amount: -5);
        var first = await service.RecordAsync(request, CancellationToken.None);

        var retry = await service.RecordAsync(
            request with { Reason = "  Helped a neighbour " },
            CancellationToken.None);

        Assert.False(retry.WasCreated);
        Assert.Equal(first.Adjustment.Id, retry.Adjustment.Id);
        Assert.Equal(0, retry.PointsBalance);
        Assert.Single(repository.Entries);
    }

    [Fact]
    public async Task A_retry_returns_the_original_balance_even_after_later_points()
    {
        var repository = new FakeRepository { Balance = 2 };
        var service = new PointAdjustmentService(repository, new FixedClock());
        var request = Request(amount: 3);
        var original = await service.RecordAsync(request, CancellationToken.None);
        repository.Entries.Add(PointsLedgerEntry.ForGoodBehaviour(
            Guid.NewGuid(), Child.Id, Guid.NewGuid(), 8, Now.AddMinutes(1)));

        var retry = await service.RecordAsync(request, CancellationToken.None);

        Assert.Equal(5, original.PointsBalance);
        Assert.False(retry.WasCreated);
        Assert.Equal(original.PointsBalance, retry.PointsBalance);
    }

    [Fact]
    public async Task A_retry_recorded_while_waiting_for_the_lock_is_replayed_not_rechecked()
    {
        var repository = new FakeRepository { Balance = 5 };
        var service = new PointAdjustmentService(repository, new FixedClock());
        var request = Request(amount: -5);
        repository.OnLockAcquired = () =>
        {
            // The original attempt took the points while this retry waited for the lock.
            var original = new PointAdjustment(
                Guid.NewGuid(), request.RequestId, Child.Id, AdultId, -5, "Helped a neighbour", Now);
            repository.Adjustments.Add(original);
            repository.Entries.Add(PointsLedgerEntry.ForManualAdjustment(
                Guid.NewGuid(), Child.Id, original.Id, -5, Now));
        };

        var result = await service.RecordAsync(request, CancellationToken.None);

        Assert.False(result.WasCreated);
        Assert.Equal(0, result.PointsBalance);
        Assert.Single(repository.Entries);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task Reusing_a_request_id_for_different_details_is_a_conflict()
    {
        var repository = new FakeRepository { Balance = 10 };
        var service = new PointAdjustmentService(repository, new FixedClock());
        var request = Request(amount: 3);
        await service.RecordAsync(request, CancellationToken.None);

        await Assert.ThrowsAsync<PointAdjustmentRequestConflictException>(() =>
            service.RecordAsync(request with { Amount = 4 }, CancellationToken.None));
        await Assert.ThrowsAsync<PointAdjustmentRequestConflictException>(() =>
            service.RecordAsync(request with { Reason = "Something else" }, CancellationToken.None));
        await Assert.ThrowsAsync<PointAdjustmentRequestConflictException>(() =>
            service.RecordAsync(
                request with { AdjustedByMemberId = Guid.NewGuid() },
                CancellationToken.None));
        await Assert.ThrowsAsync<PointAdjustmentRequestConflictException>(() =>
            service.RecordAsync(request with { ChildId = Guid.NewGuid() }, CancellationToken.None));
        Assert.Single(repository.Entries);
    }

    [Fact]
    public async Task Concurrent_duplicate_request_resolves_to_the_winning_adjustment()
    {
        var repository = new FakeRepository { Balance = 5 };
        var service = new PointAdjustmentService(repository, new FixedClock());
        var request = Request(amount: 2);
        repository.SimulateConcurrentWinner = () =>
        {
            var winner = new PointAdjustment(
                Guid.NewGuid(), request.RequestId, Child.Id, AdultId, 2, "Helped a neighbour", Now);
            repository.Adjustments.Add(winner);
            repository.Entries.Add(PointsLedgerEntry.ForManualAdjustment(
                Guid.NewGuid(), Child.Id, winner.Id, 2, Now));
        };

        var result = await service.RecordAsync(request, CancellationToken.None);

        Assert.False(result.WasCreated);
        Assert.Single(repository.Entries);
    }

    [Fact]
    public async Task A_mistake_is_corrected_by_a_new_compensating_entry_and_history_is_kept()
    {
        var repository = new FakeRepository { Balance = 0 };
        var service = new PointAdjustmentService(repository, new FixedClock());
        var mistake = await service.RecordAsync(Request(amount: 20), CancellationToken.None);

        var correction = await service.RecordAsync(
            Request(amount: -20) with { Reason = "Correcting a mistaken +20" },
            CancellationToken.None);

        Assert.Equal(0, correction.PointsBalance);
        Assert.Equal(2, repository.Entries.Count);
        Assert.Contains(repository.Entries, entry => entry.PointAdjustmentId == mistake.Adjustment.Id);
        Assert.Equal(2, repository.Adjustments.Count);
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    private static RecordPointAdjustment Request(int amount) =>
        new(Guid.NewGuid(), AdultId, Child.Id, amount, "Helped a neighbour");

    private sealed class FixedClock : IHouseholdClock
    {
        public DateOnly Today => new(2026, 9, 21);

        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeRepository : IPointAdjustmentRepository
    {
        private (PointAdjustment Adjustment, PointsLedgerEntry Entry)? _pending;

        public int Balance { get; set; }

        public List<PointAdjustment> Adjustments { get; } = [];

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

        public Task<PointAdjustment?> GetAdjustmentByRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Adjustments.SingleOrDefault(item => item.RequestId == requestId));

        public Task AddAdjustmentAsync(
            PointAdjustment adjustment,
            PointsLedgerEntry entry,
            CancellationToken cancellationToken)
        {
            _pending = (adjustment, entry);
            return Task.CompletedTask;
        }

        public Task<int> GetPointsBalanceAsync(Guid childId, CancellationToken cancellationToken) =>
            Task.FromResult(Balance + Entries.Where(entry => entry.ChildId == childId).Sum(entry => entry.Amount));

        public Task<int> GetPointsBalanceAfterAdjustmentAsync(
            Guid adjustmentId,
            CancellationToken cancellationToken)
        {
            // Entries are kept in ledger order, so the balance-after is a running sum.
            var through = Entries.FindIndex(entry => entry.PointAdjustmentId == adjustmentId);
            return Task.FromResult(Balance + Entries.Take(through + 1).Sum(entry => entry.Amount));
        }

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
                    throw new DuplicatePointAdjustmentRequestException();
                }

                Adjustments.Add(pending.Adjustment);
                Entries.Add(pending.Entry);
            }

            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
