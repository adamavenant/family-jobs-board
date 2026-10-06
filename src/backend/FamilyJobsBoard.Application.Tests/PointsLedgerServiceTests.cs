using FamilyJobsBoard.Application.Points;
using FamilyJobsBoard.Domain.Households;
using Xunit;

namespace FamilyJobsBoard.Application.Tests;

public sealed class PointsLedgerServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);

    private readonly HouseholdMember _adult = Member("Addie", HouseholdRole.Adult);
    private readonly HouseholdMember _fredster = Member("Fredster", HouseholdRole.Child);
    private readonly HouseholdMember _harrie = Member("Harrie", HouseholdRole.Child);

    [Fact]
    public async Task A_child_always_gets_only_their_own_ledger()
    {
        var repository = Repository();
        repository.Add(_fredster, "Feed the dog", 5, Start);
        repository.Add(_harrie, "Pack school bag", 8, Start.AddMinutes(1));
        var service = new PointsLedgerService(repository);

        var ledger = await service.GetAsync(_fredster.Id, null, null, CancellationToken.None);
        var sameLedger = await service.GetAsync(_fredster.Id, _fredster.Id, null, CancellationToken.None);

        Assert.Equal(_fredster.Id, ledger.SelectedChildId);
        var child = Assert.Single(ledger.Children);
        Assert.Equal(new PointsLedgerChild(_fredster.Id, "Fredster", true, 5), child);
        Assert.Equal("Feed the dog", Assert.Single(ledger.Entries).Name);
        Assert.Equal(ledger.Entries, sameLedger.Entries);
    }

    [Fact]
    public async Task Each_entry_carries_the_adult_who_recorded_it()
    {
        var repository = Repository();
        repository.Add(_fredster, "Feed the dog", 5, Start);
        repository.Add(_fredster, "Ice cream", -3, Start.AddMinutes(1), recordedBy: "Addie");
        var service = new PointsLedgerService(repository);

        var ledger = await service.GetAsync(_fredster.Id, null, null, CancellationToken.None);

        Assert.Collection(
            ledger.Entries,
            redemption =>
            {
                Assert.Equal("Ice cream", redemption.Name);
                Assert.Equal("Addie", redemption.RecordedByDisplayName);
                Assert.Equal(2, redemption.BalanceAfter);
            },
            jobAward => Assert.Null(jobAward.RecordedByDisplayName));
    }

    [Fact]
    public async Task A_child_cannot_ask_for_another_childs_ledger()
    {
        var service = new PointsLedgerService(Repository());

        await Assert.ThrowsAsync<PointsLedgerForbiddenException>(() =>
            service.GetAsync(_fredster.Id, _harrie.Id, null, CancellationToken.None));
        await Assert.ThrowsAsync<PointsLedgerForbiddenException>(() =>
            service.GetAsync(_fredster.Id, Guid.NewGuid(), null, CancellationToken.None));
    }

    [Fact]
    public async Task A_viewer_who_is_not_an_active_member_is_forbidden()
    {
        var service = new PointsLedgerService(Repository());

        await Assert.ThrowsAsync<PointsLedgerForbiddenException>(() =>
            service.GetAsync(Guid.NewGuid(), null, null, CancellationToken.None));
    }

    [Fact]
    public async Task An_adult_sees_every_child_newest_first_with_each_balance()
    {
        var repository = Repository();
        repository.Add(_fredster, "Feed the dog", 5, Start);
        repository.Add(_harrie, "Being Brave", 10, Start.AddMinutes(1));
        repository.Add(_fredster, "Broke a plate", -2, Start.AddMinutes(2));
        var service = new PointsLedgerService(repository);

        var ledger = await service.GetAsync(_adult.Id, null, null, CancellationToken.None);

        Assert.Null(ledger.SelectedChildId);
        Assert.Equal(
            [("Fredster", 3), ("Harrie", 10)],
            ledger.Children.Select(child => (child.DisplayName, child.Balance)));
        Assert.Equal(
            [("Broke a plate", "Fredster", -2, 3), ("Being Brave", "Harrie", 10, 10), ("Feed the dog", "Fredster", 5, 5)],
            ledger.Entries.Select(entry => (entry.Name, entry.ChildDisplayName, entry.Points, entry.BalanceAfter)));
        Assert.Null(ledger.NextCursor);
    }

    [Fact]
    public async Task An_adult_can_filter_to_one_child()
    {
        var repository = Repository();
        repository.Add(_fredster, "Feed the dog", 5, Start);
        repository.Add(_harrie, "Being Brave", 10, Start.AddMinutes(1));
        var service = new PointsLedgerService(repository);

        var ledger = await service.GetAsync(_adult.Id, _harrie.Id, null, CancellationToken.None);

        Assert.Equal(_harrie.Id, ledger.SelectedChildId);
        Assert.Equal(2, ledger.Children.Count);
        Assert.Equal("Being Brave", Assert.Single(ledger.Entries).Name);
    }

    [Fact]
    public async Task An_adult_filtering_by_an_unknown_member_or_an_adult_is_rejected()
    {
        var service = new PointsLedgerService(Repository());

        var unknown = await Assert.ThrowsAsync<InvalidPointsLedgerRequestException>(() =>
            service.GetAsync(_adult.Id, Guid.NewGuid(), null, CancellationToken.None));
        var adult = await Assert.ThrowsAsync<InvalidPointsLedgerRequestException>(() =>
            service.GetAsync(_adult.Id, _adult.Id, null, CancellationToken.None));

        Assert.Contains("ChildId", unknown.Errors.Keys);
        Assert.Contains("ChildId", adult.Errors.Keys);
    }

    [Fact]
    public async Task A_malformed_cursor_is_rejected()
    {
        var service = new PointsLedgerService(Repository());

        var exception = await Assert.ThrowsAsync<InvalidPointsLedgerRequestException>(() =>
            service.GetAsync(_adult.Id, null, "not-a-cursor", CancellationToken.None));

        Assert.Contains("Before", exception.Errors.Keys);
    }

    [Fact]
    public async Task Deactivated_children_are_listed_after_active_ones_only_when_they_have_entries()
    {
        var formerChild = Member("Anna", HouseholdRole.Child);
        formerChild.Deactivate(_adult.Id, Start);
        var neverEarned = Member("Bert", HouseholdRole.Child);
        neverEarned.Deactivate(_adult.Id, Start);
        var repository = Repository(formerChild, neverEarned);
        repository.Add(formerChild, "Feed the dog", 4, Start);
        var service = new PointsLedgerService(repository);

        var ledger = await service.GetAsync(_adult.Id, null, null, CancellationToken.None);
        var filtered = await service.GetAsync(_adult.Id, formerChild.Id, null, CancellationToken.None);

        Assert.Equal(
            [("Fredster", true), ("Harrie", true), ("Anna", false)],
            ledger.Children.Select(child => (child.DisplayName, child.IsActive)));
        Assert.Equal("Anna", Assert.Single(filtered.Entries).ChildDisplayName);
    }

    [Fact]
    public async Task Pages_hold_twenty_entries_and_balances_continue_across_pages()
    {
        var repository = Repository();
        for (var index = 0; index < 45; index++)
        {
            var child = index % 3 == 0 ? _harrie : _fredster;
            // Pairs of entries share a timestamp so the ID tie-break is exercised.
            repository.Add(child, $"Entry {index}", index % 5 == 0 ? -1 : 2, Start.AddMinutes(index / 2));
        }

        var service = new PointsLedgerService(repository);
        var lines = new List<PointsLedgerLine>();
        string? cursor = null;
        var pageSizes = new List<int>();
        do
        {
            var page = await service.GetAsync(_adult.Id, null, cursor, CancellationToken.None);
            pageSizes.Add(page.Entries.Count);
            lines.AddRange(page.Entries);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal([20, 20, 5], pageSizes);
        Assert.Equal(repository.Ordered().Select(record => record.Id), lines.Select(line => line.Id));
        foreach (var child in new[] { _fredster, _harrie })
        {
            var childLines = lines.Where(line => line.ChildId == child.Id).ToArray();
            Assert.Equal(repository.Balance(child.Id), childLines[0].BalanceAfter);
            for (var index = 0; index < childLines.Length - 1; index++)
            {
                Assert.Equal(
                    childLines[index].BalanceAfter - childLines[index].Points,
                    childLines[index + 1].BalanceAfter);
            }

            Assert.Equal(childLines[^1].Points, childLines[^1].BalanceAfter);
        }
    }

    [Fact]
    public void A_ledger_position_round_trips_through_its_cursor_text()
    {
        var position = new PointsLedgerPosition(
            new DateTimeOffset(2026, 9, 27, 10, 15, 30, 123, TimeSpan.Zero).AddTicks(4560),
            Guid.NewGuid());

        Assert.True(PointsLedgerPosition.TryParse(position.ToString(), out var parsed));
        Assert.Equal(position, parsed);
        Assert.False(PointsLedgerPosition.TryParse("", out _));
        Assert.False(PointsLedgerPosition.TryParse("-" + Guid.NewGuid().ToString("N"), out _));
        Assert.False(PointsLedgerPosition.TryParse("123-not-a-guid", out _));
        Assert.False(PointsLedgerPosition.TryParse($"{long.MaxValue}-{Guid.NewGuid():N}", out _));
    }

    private FakeRepository Repository(params HouseholdMember[] extraChildren) =>
        new([_adult, _fredster, _harrie, .. extraChildren]);

    private static HouseholdMember Member(string firstName, HouseholdRole role) =>
        new(Guid.NewGuid(), firstName, "Avenant", role);

    private sealed class FakeRepository : IPointsLedgerRepository
    {
        private readonly IReadOnlyList<HouseholdMember> _members;
        private readonly List<PointsLedgerRecord> _records = [];

        public FakeRepository(IReadOnlyList<HouseholdMember> members)
        {
            _members = members;
        }

        public void Add(
            HouseholdMember child,
            string name,
            int amount,
            DateTimeOffset awardedAtUtc,
            string? recordedBy = null) =>
            _records.Add(new PointsLedgerRecord(
                Guid.NewGuid(), child.Id, name, amount, awardedAtUtc, recordedBy));

        public IEnumerable<PointsLedgerRecord> Ordered() =>
            _records.OrderByDescending(record => record.AwardedAtUtc).ThenByDescending(record => record.Id);

        public int Balance(Guid childId) =>
            _records.Where(record => record.ChildId == childId).Sum(record => record.Amount);

        public Task<HouseholdMember?> GetActiveMemberAsync(
            Guid memberId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_members.SingleOrDefault(member => member.Id == memberId && member.IsActive));

        public Task<IReadOnlyList<HouseholdMember>> GetChildrenAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HouseholdMember>>(
                _members.Where(member => !member.IsAdult).OrderBy(member => member.FirstName).ToArray());

        public Task<IReadOnlyDictionary<Guid, int>> GetBalancesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, int>>(_records
                .GroupBy(record => record.ChildId)
                .ToDictionary(group => group.Key, group => group.Sum(record => record.Amount)));

        public Task<IReadOnlyList<PointsLedgerRecord>> GetEntriesAsync(
            Guid? childId,
            PointsLedgerPosition? before,
            int take,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PointsLedgerRecord>>(Ordered()
                .Where(record => childId is null || record.ChildId == childId)
                .Where(record => before is not { } position || IsBefore(record, position))
                .Take(take)
                .ToArray());

        public Task<IReadOnlyDictionary<Guid, int>> GetBalancesThroughAsync(
            IReadOnlyCollection<Guid> childIds,
            PointsLedgerPosition through,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, int>>(_records
                .Where(record => childIds.Contains(record.ChildId)
                    && (IsBefore(record, through) || record.Id == through.Id))
                .GroupBy(record => record.ChildId)
                .ToDictionary(group => group.Key, group => group.Sum(record => record.Amount)));

        private static bool IsBefore(PointsLedgerRecord record, PointsLedgerPosition position) =>
            record.AwardedAtUtc < position.AwardedAtUtc
            || (record.AwardedAtUtc == position.AwardedAtUtc && record.Id.CompareTo(position.Id) < 0);
    }
}
