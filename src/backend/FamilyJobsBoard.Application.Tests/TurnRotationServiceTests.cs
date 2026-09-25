using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Application.TurnRotations;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.TurnRotations;
using Xunit;

namespace FamilyJobsBoard.Application.Tests;

public sealed class TurnRotationServiceTests
{
    private static readonly HouseholdMember Adult = new(
        Guid.Parse("d55b77d7-a514-4071-a004-f5033aff2f9d"), "Addie", "Avenant", HouseholdRole.Adult);
    private static readonly HouseholdMember ChildA = new(
        Guid.Parse("96e7d927-dfb6-480c-89be-a5c58144f603"), "Alba", "Avenant", HouseholdRole.Child);
    private static readonly HouseholdMember ChildB = new(
        Guid.Parse("3fdb1469-df09-420e-8eeb-330717c710fe"), "Ben", "Avenant", HouseholdRole.Child);
    private static readonly HouseholdMember ChildC = new(
        Guid.Parse("9db319c1-28d1-4ce6-93d7-f04a45f8257d"), "Cass", "Avenant", HouseholdRole.Child);
    private static readonly HouseholdMember InactiveChild = new(
        Guid.NewGuid(), "Gone", "Avenant", HouseholdRole.Child, isActive: false);

    private static readonly DateOnly Today = new(2026, 9, 21);

    [Fact]
    public async Task Get_turn_returns_null_when_nothing_is_configured()
    {
        var service = CreateService(new FakeRepository());

        var turn = await service.GetTurnAsync(Today, CancellationToken.None);

        Assert.Null(turn);
    }

    [Fact]
    public async Task Get_turn_cycles_through_the_configured_order()
    {
        var repository = new FakeRepository();
        repository.Revisions.Add(new TurnRotationRevision(
            Guid.NewGuid(), Today, null, [ChildA.Id, ChildB.Id, ChildC.Id], ChildA.Id, Adult.Id,
            Today.ToDateTime(TimeOnly.MinValue)));
        var service = CreateService(repository);

        Assert.Equal(ChildA.Id, (await service.GetTurnAsync(Today, CancellationToken.None))!.ChildId);
        Assert.Equal(ChildB.Id, (await service.GetTurnAsync(Today.AddDays(1), CancellationToken.None))!.ChildId);
        Assert.Equal(ChildC.Id, (await service.GetTurnAsync(Today.AddDays(2), CancellationToken.None))!.ChildId);
        Assert.Equal(ChildA.Id, (await service.GetTurnAsync(Today.AddDays(3), CancellationToken.None))!.ChildId);
    }

    [Fact]
    public async Task A_revision_scheduled_for_tomorrow_leaves_today_unchanged()
    {
        var repository = new FakeRepository();
        var original = new TurnRotationRevision(
            Guid.NewGuid(), Today.AddDays(-10), null, [ChildA.Id, ChildB.Id], ChildA.Id, Adult.Id,
            Today.ToDateTime(TimeOnly.MinValue));
        repository.Revisions.Add(original);
        var service = CreateService(repository);

        var saved = await service.SaveAsync(
            Adult.Id,
            new SaveTurnRotation([ChildC.Id, ChildB.Id], ChildC.Id, Today.AddDays(1), "Who's up?"),
            CancellationToken.None);

        var todayTurn = await service.GetTurnAsync(Today, CancellationToken.None);
        var tomorrowTurn = await service.GetTurnAsync(Today.AddDays(1), CancellationToken.None);

        Assert.Equal(original.GetAssignedChildId(Today), todayTurn!.ChildId);
        Assert.Equal(ChildC.Id, tomorrowTurn!.ChildId);
        Assert.Equal("Who's up?", tomorrowTurn.Question);
        Assert.NotNull(saved.Current);
    }

    [Fact]
    public async Task Reconfiguring_never_rewrites_dates_before_the_new_revisions_effective_date()
    {
        var repository = new FakeRepository();
        var original = new TurnRotationRevision(
            Guid.NewGuid(), Today.AddDays(-30), null, [ChildA.Id, ChildB.Id, ChildC.Id], ChildA.Id, Adult.Id,
            Today.ToDateTime(TimeOnly.MinValue));
        repository.Revisions.Add(original);
        var service = CreateService(repository);
        var yesterday = Today.AddDays(-1);
        var originalYesterdayAnswer = original.GetAssignedChildId(yesterday);

        await service.SaveAsync(
            Adult.Id,
            new SaveTurnRotation([ChildB.Id], ChildB.Id, Today.AddDays(1), null),
            CancellationToken.None);

        var historicalTurn = await service.GetTurnAsync(yesterday, CancellationToken.None);

        Assert.Equal(originalYesterdayAnswer, historicalTurn!.ChildId);
    }

    [Fact]
    public async Task Save_rejects_a_non_adult_actor()
    {
        var service = CreateService(new FakeRepository());

        await Assert.ThrowsAsync<TurnRotationForbiddenException>(() => service.SaveAsync(
            ChildA.Id,
            new SaveTurnRotation([ChildA.Id], ChildA.Id, Today, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Save_rejects_no_participants_duplicate_participants_and_an_invalid_first_child()
    {
        var service = CreateService(new FakeRepository());

        var noParticipants = await Assert.ThrowsAsync<InvalidTurnRotationException>(() => service.SaveAsync(
            Adult.Id, new SaveTurnRotation([], ChildA.Id, Today, null), CancellationToken.None));
        Assert.Contains("ParticipantChildIds", noParticipants.Errors.Keys);

        var duplicates = await Assert.ThrowsAsync<InvalidTurnRotationException>(() => service.SaveAsync(
            Adult.Id,
            new SaveTurnRotation([ChildA.Id, ChildA.Id], ChildA.Id, Today, null),
            CancellationToken.None));
        Assert.Contains("ParticipantChildIds", duplicates.Errors.Keys);

        var badFirstChild = await Assert.ThrowsAsync<InvalidTurnRotationException>(() => service.SaveAsync(
            Adult.Id,
            new SaveTurnRotation([ChildA.Id, ChildB.Id], ChildC.Id, Today, null),
            CancellationToken.None));
        Assert.Contains("FirstChildId", badFirstChild.Errors.Keys);
    }

    [Fact]
    public async Task Save_rejects_an_inactive_or_non_child_participant()
    {
        var service = CreateService(new FakeRepository());

        var exception = await Assert.ThrowsAsync<InvalidTurnRotationException>(() => service.SaveAsync(
            Adult.Id,
            new SaveTurnRotation(
                [ChildA.Id, InactiveChild.Id, Adult.Id],
                ChildA.Id,
                Today,
                null),
            CancellationToken.None));

        Assert.Contains("ParticipantChildIds", exception.Errors.Keys);
    }

    [Fact]
    public async Task Initial_configuration_may_be_effective_today_but_a_later_change_may_not()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);

        var initial = await service.SaveAsync(
            Adult.Id, new SaveTurnRotation([ChildA.Id], ChildA.Id, Today, null), CancellationToken.None);
        Assert.NotNull(initial.Current);

        var rejectedToday = await Assert.ThrowsAsync<InvalidTurnRotationException>(() => service.SaveAsync(
            Adult.Id,
            new SaveTurnRotation([ChildB.Id], ChildB.Id, Today, null),
            CancellationToken.None));
        Assert.Contains("EffectiveFrom", rejectedToday.Errors.Keys);

        var accepted = await service.SaveAsync(
            Adult.Id,
            new SaveTurnRotation([ChildB.Id], ChildB.Id, Today.AddDays(1), null),
            CancellationToken.None);
        Assert.NotNull(accepted.Current);
    }

    [Fact]
    public async Task Overview_includes_an_upcoming_preview()
    {
        var repository = new FakeRepository();
        repository.Revisions.Add(new TurnRotationRevision(
            Guid.NewGuid(), Today, null, [ChildA.Id, ChildB.Id], ChildA.Id, Adult.Id,
            Today.ToDateTime(TimeOnly.MinValue)));
        var service = CreateService(repository);

        var overview = await service.GetOverviewAsync(CancellationToken.None);

        Assert.NotEmpty(overview.UpcomingTurns);
        Assert.Equal(Today, overview.UpcomingTurns[0].Date);
        Assert.Equal(ChildA.Id, overview.UpcomingTurns[0].ChildId);
    }

    private static TurnRotationService CreateService(FakeRepository repository) =>
        new(repository, new FixedClock());

    private sealed class FixedClock : IHouseholdClock
    {
        public DateOnly Today => TurnRotationServiceTests.Today;

        public DateTimeOffset UtcNow => new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeRepository : ITurnRotationRepository
    {
        private static readonly HouseholdMember[] Members =
            [Adult, ChildA, ChildB, ChildC, InactiveChild];

        public List<TurnRotationRevision> Revisions { get; } = [];

        public Task<IReadOnlyList<TurnRotationRevision>> GetRevisionsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TurnRotationRevision>>([.. Revisions]);

        public Task<HouseholdMember?> GetMemberAsync(Guid memberId, CancellationToken cancellationToken) =>
            Task.FromResult(Members.SingleOrDefault(member => member.Id == memberId));

        public Task<IReadOnlyList<HouseholdMember>> GetActiveChildrenAsync(
            IReadOnlyCollection<Guid> childIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HouseholdMember>>(
                Members.Where(member =>
                        childIds.Contains(member.Id) && member.IsActive && !member.IsAdult)
                    .ToArray());

        public Task AddRevisionAsync(TurnRotationRevision revision, CancellationToken cancellationToken)
        {
            Revisions.Add(revision);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
