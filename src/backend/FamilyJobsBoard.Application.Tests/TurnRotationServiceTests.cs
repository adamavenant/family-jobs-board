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

    private static readonly Guid Pink = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid NextToMum = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateOnly Today = new(2026, 9, 21);
    private static readonly DateTimeOffset Created = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_turns_is_empty_when_nothing_is_configured()
    {
        var service = CreateService(new FakeRepository());

        Assert.Empty(await service.GetTurnsAsync(Today, CancellationToken.None));
    }

    [Fact]
    public async Task Get_turns_cycles_through_the_configured_order()
    {
        var repository = new FakeRepository();
        repository.Add(Pink, Today, [ChildA, ChildB, ChildC], ChildA);
        var service = CreateService(repository);

        Assert.Equal(ChildA.Id, await TurnOn(service, Today));
        Assert.Equal(ChildB.Id, await TurnOn(service, Today.AddDays(1)));
        Assert.Equal(ChildC.Id, await TurnOn(service, Today.AddDays(2)));
        Assert.Equal(ChildA.Id, await TurnOn(service, Today.AddDays(3)));
    }

    [Fact]
    public async Task Several_rotations_run_independently_side_by_side()
    {
        var repository = new FakeRepository();
        repository.Add(Pink, Today, [ChildA, ChildB], ChildA, "Who is Pink today?", Created);
        repository.Add(NextToMum, Today, [ChildA, ChildB], ChildB, "Who sits next to Mum?", Created.AddMinutes(1));
        var service = CreateService(repository);

        var turns = await service.GetTurnsAsync(Today, CancellationToken.None);

        Assert.Equal(2, turns.Count);
        Assert.Equal(("Who is Pink today?", ChildA.Id), (turns[0].Question, turns[0].ChildId));
        Assert.Equal(("Who sits next to Mum?", ChildB.Id), (turns[1].Question, turns[1].ChildId));
        var tomorrow = await service.GetTurnsAsync(Today.AddDays(1), CancellationToken.None);
        Assert.Equal([ChildB.Id, ChildA.Id], tomorrow.Select(turn => turn.ChildId));
    }

    [Fact]
    public async Task Creating_a_rotation_leaves_existing_rotations_untouched()
    {
        var repository = new FakeRepository();
        repository.Add(Pink, Today.AddDays(-5), [ChildA, ChildB], ChildA);
        var service = CreateService(repository);
        var before = await TurnOn(service, Today);

        var overview = await service.SaveAsync(
            Adult.Id,
            null,
            new SaveTurnRotation([ChildC.Id], ChildC.Id, Today, "Who sits next to Mum?"),
            CancellationToken.None);

        Assert.Equal(2, overview.Rotations.Count);
        Assert.Equal(before, (await service.GetTurnsAsync(Today, CancellationToken.None))[0].ChildId);
        Assert.Equal(ChildC.Id, (await service.GetTurnsAsync(Today, CancellationToken.None))[1].ChildId);
    }

    [Fact]
    public async Task A_revision_scheduled_for_tomorrow_leaves_today_unchanged()
    {
        var repository = new FakeRepository();
        var original = repository.Add(Pink, Today.AddDays(-10), [ChildA, ChildB], ChildA);
        var service = CreateService(repository);

        await service.SaveAsync(
            Adult.Id,
            Pink,
            new SaveTurnRotation([ChildC.Id, ChildB.Id], ChildC.Id, Today.AddDays(1), "Who's up?"),
            CancellationToken.None);

        Assert.Equal(original.GetAssignedChildId(Today)!.Value, await TurnOn(service, Today));
        var tomorrow = Assert.Single(await service.GetTurnsAsync(Today.AddDays(1), CancellationToken.None));
        Assert.Equal(ChildC.Id, tomorrow.ChildId);
        Assert.Equal("Who's up?", tomorrow.Question);
    }

    [Fact]
    public async Task Reconfiguring_never_rewrites_dates_before_the_new_revisions_effective_date()
    {
        var repository = new FakeRepository();
        var original = repository.Add(Pink, Today.AddDays(-30), [ChildA, ChildB, ChildC], ChildA);
        var service = CreateService(repository);
        var yesterday = Today.AddDays(-1);

        await service.SaveAsync(
            Adult.Id,
            Pink,
            new SaveTurnRotation([ChildB.Id], ChildB.Id, Today.AddDays(1), null),
            CancellationToken.None);

        Assert.Equal(original.GetAssignedChildId(yesterday)!.Value, await TurnOn(service, yesterday));
    }

    [Fact]
    public async Task Ending_a_rotation_hides_it_from_tomorrow_but_keeps_today_and_others()
    {
        var repository = new FakeRepository();
        repository.Add(Pink, Today.AddDays(-2), [ChildA, ChildB], ChildA, created: Created);
        repository.Add(NextToMum, Today.AddDays(-2), [ChildA], ChildA, created: Created.AddMinutes(1));
        var service = CreateService(repository);

        var overview = await service.EndAsync(Adult.Id, Pink, CancellationToken.None);

        Assert.Equal(2, (await service.GetTurnsAsync(Today, CancellationToken.None)).Count);
        var tomorrow = Assert.Single(await service.GetTurnsAsync(Today.AddDays(1), CancellationToken.None));
        Assert.Equal(NextToMum, tomorrow.RotationId);
        Assert.Equal(2, overview.Rotations.Count);
        await Assert.ThrowsAsync<TurnRotationNotFoundException>(() =>
            service.EndAsync(Adult.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Updating_an_unknown_rotation_is_not_found()
    {
        var service = CreateService(new FakeRepository());

        await Assert.ThrowsAsync<TurnRotationNotFoundException>(() => service.SaveAsync(
            Adult.Id,
            Guid.NewGuid(),
            new SaveTurnRotation([ChildA.Id], ChildA.Id, Today.AddDays(1), null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Non_adults_cannot_save_or_end_rotations()
    {
        var repository = new FakeRepository();
        repository.Add(Pink, Today, [ChildA], ChildA);
        var service = CreateService(repository);

        await Assert.ThrowsAsync<TurnRotationForbiddenException>(() => service.SaveAsync(
            ChildA.Id, null, new SaveTurnRotation([ChildA.Id], ChildA.Id, Today, null), CancellationToken.None));
        await Assert.ThrowsAsync<TurnRotationForbiddenException>(() =>
            service.EndAsync(ChildA.Id, Pink, CancellationToken.None));
    }

    [Fact]
    public async Task Save_rejects_no_participants_duplicate_participants_and_an_invalid_first_child()
    {
        var service = CreateService(new FakeRepository());

        var noParticipants = await Assert.ThrowsAsync<InvalidTurnRotationException>(() => service.SaveAsync(
            Adult.Id, null, new SaveTurnRotation([], ChildA.Id, Today, null), CancellationToken.None));
        Assert.Contains("ParticipantChildIds", noParticipants.Errors.Keys);

        var duplicates = await Assert.ThrowsAsync<InvalidTurnRotationException>(() => service.SaveAsync(
            Adult.Id,
            null,
            new SaveTurnRotation([ChildA.Id, ChildA.Id], ChildA.Id, Today, null),
            CancellationToken.None));
        Assert.Contains("ParticipantChildIds", duplicates.Errors.Keys);

        var badFirstChild = await Assert.ThrowsAsync<InvalidTurnRotationException>(() => service.SaveAsync(
            Adult.Id,
            null,
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
            null,
            new SaveTurnRotation([ChildA.Id, InactiveChild.Id, Adult.Id], ChildA.Id, Today, null),
            CancellationToken.None));

        Assert.Contains("ParticipantChildIds", exception.Errors.Keys);
    }

    [Fact]
    public async Task A_new_rotation_may_start_today_but_a_change_to_it_may_not()
    {
        var service = CreateService(new FakeRepository());

        var initial = await service.SaveAsync(
            Adult.Id, null, new SaveTurnRotation([ChildA.Id], ChildA.Id, Today, null), CancellationToken.None);
        var rotationId = Assert.Single(initial.Rotations).RotationId;

        var rejectedToday = await Assert.ThrowsAsync<InvalidTurnRotationException>(() => service.SaveAsync(
            Adult.Id,
            rotationId,
            new SaveTurnRotation([ChildB.Id], ChildB.Id, Today, null),
            CancellationToken.None));
        Assert.Contains("EffectiveFrom", rejectedToday.Errors.Keys);

        var accepted = await service.SaveAsync(
            Adult.Id,
            rotationId,
            new SaveTurnRotation([ChildB.Id], ChildB.Id, Today.AddDays(1), null),
            CancellationToken.None);
        Assert.NotNull(Assert.Single(accepted.Rotations).Current);

        var second = await service.SaveAsync(
            Adult.Id, null, new SaveTurnRotation([ChildB.Id], ChildB.Id, Today, null), CancellationToken.None);
        Assert.Equal(2, second.Rotations.Count);
    }

    [Fact]
    public async Task Overview_includes_an_upcoming_preview_per_rotation()
    {
        var repository = new FakeRepository();
        repository.Add(Pink, Today, [ChildA, ChildB], ChildA);
        var service = CreateService(repository);

        var rotation = Assert.Single((await service.GetOverviewAsync(CancellationToken.None)).Rotations);

        Assert.Equal(Today, rotation.UpcomingTurns[0].Date);
        Assert.Equal(ChildA.Id, rotation.UpcomingTurns[0].ChildId);
    }

    [Fact]
    public async Task Removing_a_participant_replaces_every_affected_rotation_from_tomorrow_and_keeps_history()
    {
        var repository = new FakeRepository();
        var pink = repository.Add(Pink, Today.AddDays(-10), [ChildA, ChildB, ChildC], ChildA);
        repository.Add(NextToMum, Today.AddDays(-10), [ChildB, ChildC], ChildB, created: Created.AddMinutes(1));
        var service = CreateService(repository);
        var todayBefore = await service.GetTurnsAsync(Today, CancellationToken.None);
        var tomorrowChild = pink.GetAssignedChildId(Today.AddDays(1))!.Value;

        await service.RemoveParticipantAsync(tomorrowChild, Adult.Id, CancellationToken.None);

        var todayAfter = await service.GetTurnsAsync(Today, CancellationToken.None);
        Assert.Equal(todayBefore.Select(turn => turn.ChildId), todayAfter.Select(turn => turn.ChildId));
        for (var offset = 1; offset < 10; offset++)
        {
            var turns = await service.GetTurnsAsync(Today.AddDays(offset), CancellationToken.None);
            Assert.DoesNotContain(turns, turn => turn.ChildId == tomorrowChild);
        }

        var next = (await service.GetTurnsAsync(Today.AddDays(1), CancellationToken.None))
            .Single(turn => turn.RotationId == Pink);
        Assert.Equal(pink.GetAssignedChildId(Today.AddDays(2))!.Value, next.ChildId);
    }

    [Fact]
    public async Task Removing_the_last_participant_makes_that_rotation_unavailable_from_tomorrow()
    {
        var repository = new FakeRepository();
        repository.Add(Pink, Today.AddDays(-1), [ChildA], ChildA);
        var service = CreateService(repository);

        await service.RemoveParticipantAsync(ChildA.Id, Adult.Id, CancellationToken.None);

        Assert.NotEmpty(await service.GetTurnsAsync(Today, CancellationToken.None));
        Assert.Empty(await service.GetTurnsAsync(Today.AddDays(1), CancellationToken.None));
    }

    [Fact]
    public async Task Answers_still_name_a_child_who_has_since_been_deactivated()
    {
        var repository = new FakeRepository();
        repository.Add(Pink, Today, [InactiveChild], InactiveChild);
        var service = CreateService(repository);

        var turn = Assert.Single(await service.GetTurnsAsync(Today, CancellationToken.None));

        Assert.Equal("Gone", turn.ChildDisplayName);
    }

    private static async Task<Guid> TurnOn(TurnRotationService service, DateOnly date) =>
        (await service.GetTurnsAsync(date, CancellationToken.None))[0].ChildId;

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

        public TurnRotationRevision Add(
            Guid rotationId,
            DateOnly effectiveFrom,
            HouseholdMember[] participants,
            HouseholdMember first,
            string? question = null,
            DateTimeOffset? created = null)
        {
            var revision = new TurnRotationRevision(
                Guid.NewGuid(),
                rotationId,
                effectiveFrom,
                question,
                participants.Select(member => member.Id).ToArray(),
                first.Id,
                Adult.Id,
                created ?? Created);
            Revisions.Add(revision);
            return revision;
        }

        public Task<IReadOnlyList<TurnRotationRevision>> GetRevisionsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TurnRotationRevision>>([.. Revisions]);

        public Task<HouseholdMember?> GetMemberAsync(Guid memberId, CancellationToken cancellationToken) =>
            Task.FromResult(Members.SingleOrDefault(member => member.Id == memberId));

        public Task<IReadOnlyList<HouseholdMember>> GetMembersAsync(
            IReadOnlyCollection<Guid> memberIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HouseholdMember>>(
                Members.Where(member => memberIds.Contains(member.Id)).ToArray());

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
