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
        Assert.Equal(14, rotation.UpcomingTurns.Count);
        Assert.Equal("daily", rotation.Current!.AdvanceMode);
        Assert.Empty(rotation.UpNext);
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

    [Fact]
    public async Task Saving_a_rotation_that_moves_on_when_ticked_previews_the_order_instead_of_dates()
    {
        var service = CreateService(new FakeRepository());

        var overview = await service.SaveAsync(
            Adult.Id,
            null,
            new SaveTurnRotation(
                [ChildA.Id, ChildB.Id, ChildC.Id], ChildB.Id, Today, "Who is first in the shower?", "whenTicked"),
            CancellationToken.None);

        var rotation = Assert.Single(overview.Rotations);
        Assert.Equal("whenTicked", rotation.Current!.AdvanceMode);
        var today = Assert.Single(rotation.UpcomingTurns);
        Assert.Equal((Today, ChildB.Id), (today.Date, today.ChildId));
        Assert.Equal([ChildB.Id, ChildC.Id, ChildA.Id], rotation.UpNext.Select(child => child.ChildId));
    }

    [Fact]
    public async Task Save_rejects_an_unknown_advance_mode()
    {
        var service = CreateService(new FakeRepository());

        var exception = await Assert.ThrowsAsync<InvalidTurnRotationException>(() => service.SaveAsync(
            Adult.Id,
            null,
            new SaveTurnRotation([ChildA.Id], ChildA.Id, Today, null, "weekly"),
            CancellationToken.None));

        Assert.Contains("AdvanceMode", exception.Errors.Keys);
    }

    [Fact]
    public async Task Three_child_rotation_shows_todays_turn_done_and_the_next_child_from_tomorrow()
    {
        var repository = new FakeRepository();
        repository.AddTicked(Pink, Today.AddDays(-3), [ChildA, ChildB, ChildC], ChildA);
        var service = CreateService(repository);
        Assert.Equal(ChildA.Id, await TurnOn(service, Today));

        var taken = await TakeAsync(service, ChildA.Id, Pink, ChildA);

        Assert.Equal((Today, ChildA.Id, "Alba", ChildA.Id), (taken.Date, taken.ChildId, taken.ChildDisplayName, taken.RecordedByMemberId));
        var today = Assert.Single(await service.GetTurnsAsync(Today, new TurnRotationViewer(ChildA.Id, false), CancellationToken.None));
        Assert.Equal((ChildA.Id, true, false), (today.ChildId, today.TurnTaken, today.CanTakeTurn));
        Assert.Equal(taken.Id, today.UndoableTurn!.TurnId);
        Assert.Equal(ChildB.Id, await TurnOn(service, Today.AddDays(1)));
        Assert.Equal(ChildB.Id, await TurnOn(service, Today.AddDays(5)));
        var second = await Assert.ThrowsAsync<TakenTurnConflictException>(() =>
            TakeAsync(service, Adult.Id, Pink, ChildA));
        Assert.Contains("already been ticked off", second.Message);
    }

    [Fact]
    public async Task Two_child_rotation_moves_on_immediately_and_offers_the_undo_to_who_may_untick()
    {
        var repository = new FakeRepository();
        repository.AddTicked(Pink, Today, [ChildA, ChildB], ChildA);
        var service = CreateService(repository);

        var taken = await TakeAsync(service, Adult.Id, Pink, ChildA);

        var forA = Assert.Single(await service.GetTurnsAsync(Today, new TurnRotationViewer(ChildA.Id, false), CancellationToken.None));
        var forB = Assert.Single(await service.GetTurnsAsync(Today, new TurnRotationViewer(ChildB.Id, false), CancellationToken.None));
        Assert.Equal((ChildB.Id, false, false), (forA.ChildId, forA.TurnTaken, forA.CanTakeTurn));
        Assert.Equal(new TurnRotationTakenTurn(taken.Id, ChildA.Id, "Alba"), forA.UndoableTurn);
        Assert.Equal((ChildB.Id, true), (forB.ChildId, forB.CanTakeTurn));
        Assert.Null(forB.UndoableTurn);

        await TakeAsync(service, ChildB.Id, Pink, ChildB);

        Assert.Equal(ChildA.Id, await TurnOn(service, Today));
    }

    [Fact]
    public async Task Only_an_adult_or_the_child_whose_turn_it_is_may_tick_it_off()
    {
        var repository = new FakeRepository();
        repository.AddTicked(Pink, Today, [ChildA, ChildB, ChildC], ChildA);
        var service = CreateService(repository);

        await Assert.ThrowsAsync<TakenTurnForbiddenException>(() =>
            TakeAsync(service, ChildB.Id, Pink, ChildA));
        await Assert.ThrowsAsync<TakenTurnForbiddenException>(() =>
            TakeAsync(service, InactiveChild.Id, Pink, InactiveChild));
        Assert.Empty(repository.TakenTurns);

        async Task<bool> CanTake(HouseholdMember viewer) =>
            (await service.GetTurnsAsync(
                Today, new TurnRotationViewer(viewer.Id, viewer.IsAdult), CancellationToken.None))[0].CanTakeTurn;
        Assert.True(await CanTake(Adult));
        Assert.True(await CanTake(ChildA));
        Assert.False(await CanTake(ChildB));
        Assert.False((await service.GetTurnsAsync(Today, CancellationToken.None))[0].CanTakeTurn);
    }

    [Fact]
    public async Task A_stale_tick_is_a_conflict_and_never_moves_the_rotation_on_twice()
    {
        var repository = new FakeRepository();
        repository.AddTicked(Pink, Today, [ChildA, ChildB], ChildA);
        var service = CreateService(repository);
        await TakeAsync(service, ChildA.Id, Pink, ChildA);

        var stale = await Assert.ThrowsAsync<TakenTurnConflictException>(() =>
            TakeAsync(service, ChildA.Id, Pink, ChildA));

        Assert.Contains("Ben's turn now", stale.Message);
        Assert.Single(repository.TakenTurns);
        Assert.Equal(ChildB.Id, await TurnOn(service, Today));
    }

    [Fact]
    public async Task Ticking_is_refused_for_a_daily_unknown_unstarted_or_ended_rotation_or_without_a_child()
    {
        var repository = new FakeRepository();
        repository.Add(Pink, Today, [ChildA, ChildB], ChildA);
        repository.AddTicked(NextToMum, Today.AddDays(2), [ChildA, ChildB], ChildA);
        var ended = Guid.NewGuid();
        repository.AddTicked(ended, Today.AddDays(-5), [ChildA], ChildA);
        repository.Revisions.Add(TurnRotationRevision.Cleared(
            Guid.NewGuid(), ended, Today, "Gone?", Adult.Id, Created));
        var service = CreateService(repository);

        var daily = await Assert.ThrowsAsync<InvalidTurnRotationException>(() =>
            TakeAsync(service, Adult.Id, Pink, ChildA));
        Assert.Contains("AdvanceMode", daily.Errors.Keys);
        await Assert.ThrowsAsync<TurnRotationNotFoundException>(() =>
            TakeAsync(service, Adult.Id, Guid.NewGuid(), ChildA));
        await Assert.ThrowsAsync<TurnRotationNotFoundException>(() =>
            TakeAsync(service, Adult.Id, ended, ChildA));
        await Assert.ThrowsAsync<TakenTurnConflictException>(() =>
            TakeAsync(service, Adult.Id, NextToMum, ChildA));
        repository.AddTicked(NextToMum, Today, [ChildA, ChildB], ChildA);
        var incomplete = await Assert.ThrowsAsync<InvalidTurnRotationException>(() =>
            service.TakeTurnAsync(
                Adult.Id, NextToMum, new TakeTurn(Guid.NewGuid(), null, null, null), CancellationToken.None));
        Assert.Equal(["ChildId", "RevisionId", "TurnNumber"], incomplete.Errors.Keys.Order());
        var noRequestId = await Assert.ThrowsAsync<InvalidTurnRotationException>(() =>
            service.TakeTurnAsync(
                Adult.Id, NextToMum, new TakeTurn(Guid.Empty, null, 0, ChildA.Id), CancellationToken.None));
        Assert.Contains("RequestId", noRequestId.Errors.Keys);
        Assert.Empty(repository.TakenTurns);
    }

    [Fact]
    public async Task Ticks_and_unticks_are_saved_while_holding_the_rotation_lock()
    {
        var repository = new FakeRepository();
        repository.AddTicked(Pink, Today, [ChildA, ChildB], ChildA);
        var service = CreateService(repository);

        var taken = await TakeAsync(service, Adult.Id, Pink, ChildA);
        await service.UndoTurnAsync(Adult.Id, Pink, taken.Id, CancellationToken.None);

        Assert.Equal(2, repository.Locks.Count);
        Assert.All(repository.Locks, rotationLock =>
        {
            Assert.Equal(Pink, rotationLock.RotationId);
            Assert.True(rotationLock.SavedWhileHeld);
            Assert.True(rotationLock.Committed);
            Assert.True(rotationLock.Disposed);
        });
    }

    [Fact]
    public async Task Unticking_restores_the_previous_answer_and_keeps_who_and_when()
    {
        var repository = new FakeRepository();
        repository.AddTicked(Pink, Today, [ChildA, ChildB, ChildC], ChildA);
        var service = CreateService(repository);
        var taken = await TakeAsync(service, ChildA.Id, Pink, ChildA);

        var undone = await service.UndoTurnAsync(ChildA.Id, Pink, taken.Id, CancellationToken.None);

        Assert.Equal(ChildA.Id, undone.UndoneByMemberId);
        Assert.NotNull(undone.UndoneAtUtc);
        Assert.Single(repository.TakenTurns);
        var today = Assert.Single(await service.GetTurnsAsync(Today, new TurnRotationViewer(ChildA.Id, false), CancellationToken.None));
        Assert.Equal((ChildA.Id, false, true), (today.ChildId, today.TurnTaken, today.CanTakeTurn));
        Assert.Null(today.UndoableTurn);
        Assert.Equal(ChildA.Id, await TurnOn(service, Today.AddDays(1)));
        await Assert.ThrowsAsync<TakenTurnConflictException>(() =>
            service.UndoTurnAsync(Adult.Id, Pink, taken.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Only_the_most_recent_tick_from_today_can_be_unticked()
    {
        var repository = new FakeRepository();
        var revision = repository.AddTicked(Pink, Today.AddDays(-1), [ChildA, ChildB], ChildA);
        var yesterday = repository.Tick(revision, Today.AddDays(-1), ChildA);
        var service = CreateService(repository);
        var first = await TakeAsync(service, Adult.Id, Pink, ChildB);
        var second = await TakeAsync(service, Adult.Id, Pink, ChildA);

        var older = await Assert.ThrowsAsync<TakenTurnConflictException>(() =>
            service.UndoTurnAsync(Adult.Id, Pink, first.Id, CancellationToken.None));
        Assert.Contains("most recent", older.Message);
        await service.UndoTurnAsync(Adult.Id, Pink, second.Id, CancellationToken.None);
        await service.UndoTurnAsync(Adult.Id, Pink, first.Id, CancellationToken.None);
        var earlierDay = await Assert.ThrowsAsync<TakenTurnConflictException>(() =>
            service.UndoTurnAsync(Adult.Id, Pink, yesterday.Id, CancellationToken.None));
        Assert.Contains("today", earlierDay.Message);
        Assert.Equal(ChildB.Id, await TurnOn(service, Today));
    }

    [Fact]
    public async Task Only_an_adult_or_the_child_whose_turn_was_ticked_may_untick_it()
    {
        var repository = new FakeRepository();
        repository.AddTicked(Pink, Today, [ChildA, ChildB], ChildA);
        repository.AddTicked(NextToMum, Today, [ChildA, ChildB], ChildA);
        var service = CreateService(repository);
        var taken = await TakeAsync(service, ChildA.Id, Pink, ChildA);

        await Assert.ThrowsAsync<TakenTurnForbiddenException>(() =>
            service.UndoTurnAsync(ChildB.Id, Pink, taken.Id, CancellationToken.None));
        await Assert.ThrowsAsync<TakenTurnNotFoundException>(() =>
            service.UndoTurnAsync(Adult.Id, NextToMum, taken.Id, CancellationToken.None));
        await Assert.ThrowsAsync<TakenTurnNotFoundException>(() =>
            service.UndoTurnAsync(Adult.Id, Pink, Guid.NewGuid(), CancellationToken.None));
        Assert.False(taken.UndoneAtUtc.HasValue);
        Assert.Equal(ChildB.Id, await TurnOn(service, Today));
    }

    [Fact]
    public async Task Past_dates_show_whose_turn_it_was_and_whether_it_was_ticked_without_tick_controls()
    {
        var repository = new FakeRepository();
        var revision = repository.AddTicked(Pink, Today.AddDays(-3), [ChildA, ChildB, ChildC], ChildA);
        repository.Tick(revision, Today.AddDays(-2), ChildA);
        var service = CreateService(repository);
        var adult = new TurnRotationViewer(Adult.Id, true);

        var ticked = Assert.Single(await service.GetTurnsAsync(Today.AddDays(-2), adult, CancellationToken.None));
        var untickedBefore = Assert.Single(await service.GetTurnsAsync(Today.AddDays(-3), adult, CancellationToken.None));
        var untickedAfter = Assert.Single(await service.GetTurnsAsync(Today.AddDays(-1), adult, CancellationToken.None));
        var future = Assert.Single(await service.GetTurnsAsync(Today.AddDays(2), adult, CancellationToken.None));

        Assert.Equal((ChildA.Id, true, false), (ticked.ChildId, ticked.TurnTaken, ticked.CanTakeTurn));
        Assert.Null(ticked.UndoableTurn);
        Assert.Equal((ChildA.Id, false), (untickedBefore.ChildId, untickedBefore.TurnTaken));
        Assert.Equal((ChildB.Id, false, false), (untickedAfter.ChildId, untickedAfter.TurnTaken, untickedAfter.CanTakeTurn));
        Assert.Equal((ChildB.Id, false, false), (future.ChildId, future.TurnTaken, future.CanTakeTurn));
    }

    [Fact]
    public async Task Overview_lists_the_order_from_whoever_takes_the_next_turn()
    {
        var repository = new FakeRepository();
        var revision = repository.AddTicked(Pink, Today.AddDays(-1), [ChildA, ChildB, ChildC], ChildA);
        repository.Tick(revision, Today, ChildA);
        var service = CreateService(repository);

        var rotation = Assert.Single((await service.GetOverviewAsync(CancellationToken.None)).Rotations);

        Assert.Equal([ChildB.Id, ChildC.Id, ChildA.Id], rotation.UpNext.Select(child => child.ChildId));
        Assert.Equal(["Ben", "Cass", "Alba"], rotation.UpNext.Select(child => child.ChildDisplayName));
        Assert.Equal(ChildA.Id, Assert.Single(rotation.UpcomingTurns).ChildId);
    }

    [Fact]
    public async Task Removing_a_participant_from_a_ticked_rotation_keeps_its_mode_and_the_next_turn()
    {
        var repository = new FakeRepository();
        var revision = repository.AddTicked(Pink, Today.AddDays(-1), [ChildA, ChildB, ChildC], ChildA);
        repository.Tick(revision, Today, ChildA);
        var service = CreateService(repository);

        await service.RemoveParticipantAsync(ChildC.Id, Adult.Id, CancellationToken.None);

        var replacement = repository.Revisions.Last();
        Assert.Equal(
            (Today.AddDays(1), TurnAdvanceMode.WhenTicked, (Guid?)ChildB.Id),
            (replacement.EffectiveFrom, replacement.AdvanceMode, replacement.FirstChildId));
        Assert.Equal([ChildA.Id, ChildB.Id], replacement.OrderedParticipantChildIds);
        var today = Assert.Single(await service.GetTurnsAsync(Today, CancellationToken.None));
        Assert.Equal((ChildA.Id, true), (today.ChildId, today.TurnTaken));
        Assert.Equal(ChildB.Id, await TurnOn(service, Today.AddDays(1)));

        await service.RemoveParticipantAsync(ChildB.Id, Adult.Id, CancellationToken.None);

        Assert.Equal((Guid?)ChildA.Id, repository.Revisions.Last().FirstChildId);
    }

    [Fact]
    public async Task A_tick_is_recorded_at_the_precision_the_database_keeps()
    {
        var repository = new FakeRepository();
        repository.AddTicked(Pink, Today, [ChildA, ChildB], ChildA);
        var clock = new FineGrainedClock();
        var service = CreateService(repository, clock);

        var taken = await TakeAsync(service, ChildA.Id, Pink, ChildA);

        var expected = clock.UtcNow.AddTicks(-(clock.UtcNow.Ticks % TimeSpan.TicksPerMicrosecond));
        Assert.NotEqual(clock.UtcNow, expected);
        Assert.Equal(expected, taken.RecordedAtUtc);
        Assert.Equal(expected, Assert.Single(repository.TakenTurns).RecordedAtUtc);
    }

    [Fact]
    public async Task A_replayed_request_returns_the_original_tick_without_ticking_again()
    {
        var repository = new FakeRepository();
        repository.AddTicked(Pink, Today, [ChildA, ChildB], ChildA);
        var service = CreateService(repository);
        var original = await ShownTurnAsync(service, Pink, ChildA);
        var taken = await service.TakeTurnAsync(ChildA.Id, Pink, original, CancellationToken.None);
        await TakeAsync(service, ChildB.Id, Pink, ChildB);

        var replayed = await service.TakeTurnAsync(ChildA.Id, Pink, original, CancellationToken.None);

        Assert.Equal(taken, replayed);
        Assert.Equal(2, repository.TakenTurns.Count);
        var today = Assert.Single(await service.GetTurnsAsync(Today, CancellationToken.None));
        Assert.Equal((ChildA.Id, 2), (today.ChildId, today.TurnNumber));
    }

    [Fact]
    public async Task A_stale_tick_for_a_childs_earlier_turn_is_a_conflict_when_their_turn_comes_round_again()
    {
        var repository = new FakeRepository();
        repository.AddTicked(Pink, Today, [ChildA, ChildB], ChildA);
        var service = CreateService(repository);
        var staleView = await ShownTurnAsync(service, Pink, ChildA);
        await service.TakeTurnAsync(ChildA.Id, Pink, staleView, CancellationToken.None);
        await TakeAsync(service, ChildB.Id, Pink, ChildB);

        var stale = await Assert.ThrowsAsync<TakenTurnConflictException>(() => service.TakeTurnAsync(
            ChildA.Id, Pink, staleView with { RequestId = Guid.NewGuid() }, CancellationToken.None));

        Assert.Contains("already moved on", stale.Message);
        Assert.Equal(2, repository.TakenTurns.Count);
        Assert.Equal(ChildA.Id, await TurnOn(service, Today));
    }

    [Fact]
    public async Task A_request_id_reused_for_a_different_tick_is_a_conflict()
    {
        var repository = new FakeRepository();
        repository.AddTicked(Pink, Today, [ChildA, ChildB], ChildA);
        var service = CreateService(repository);
        var first = await ShownTurnAsync(service, Pink, ChildA);
        await service.TakeTurnAsync(Adult.Id, Pink, first, CancellationToken.None);
        var next = await ShownTurnAsync(service, Pink, ChildB);

        var reused = await Assert.ThrowsAsync<TakenTurnConflictException>(() => service.TakeTurnAsync(
            Adult.Id, Pink, next with { RequestId = first.RequestId }, CancellationToken.None));

        Assert.Contains("request ID", reused.Message);
        Assert.Single(repository.TakenTurns);
    }

    private static async Task<Guid> TurnOn(TurnRotationService service, DateOnly date) =>
        (await service.GetTurnsAsync(date, CancellationToken.None))[0].ChildId;

    /// <summary>
    /// A tick for the turn the board shows today, as a client sends it; a rotation with no
    /// answer today gets a made-up revision.
    /// </summary>
    private static async Task<TakeTurn> ShownTurnAsync(
        TurnRotationService service,
        Guid rotationId,
        HouseholdMember child)
    {
        var shown = (await service.GetTurnsAsync(Today, CancellationToken.None))
            .SingleOrDefault(turn => turn.RotationId == rotationId);
        return new TakeTurn(Guid.NewGuid(), shown?.RevisionId ?? Guid.NewGuid(), shown?.TurnNumber ?? 0, child.Id);
    }

    private static async Task<TakenTurnDetails> TakeAsync(
        TurnRotationService service,
        Guid actorMemberId,
        Guid rotationId,
        HouseholdMember child) =>
        await service.TakeTurnAsync(
            actorMemberId, rotationId, await ShownTurnAsync(service, rotationId, child), CancellationToken.None);

    private static TurnRotationService CreateService(FakeRepository repository, IHouseholdClock? clock = null) =>
        new(repository, clock ?? new FixedClock());

    private class FixedClock : IHouseholdClock
    {
        public DateOnly Today => TurnRotationServiceTests.Today;

        public virtual DateTimeOffset UtcNow => new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);
    }

    /// <summary>A clock with sub-microsecond ticks, as on Linux, which PostgreSQL cannot store.</summary>
    private sealed class FineGrainedClock : FixedClock
    {
        public override DateTimeOffset UtcNow => base.UtcNow.AddTicks(1_234_567);
    }

    private sealed class FakeRepository : ITurnRotationRepository
    {
        private static readonly HouseholdMember[] Members =
            [Adult, ChildA, ChildB, ChildC, InactiveChild];

        public List<TurnRotationRevision> Revisions { get; } = [];

        public List<TakenTurn> TakenTurns { get; } = [];

        public List<FakeTurnRotationLock> Locks { get; } = [];

        public TurnRotationRevision Add(
            Guid rotationId,
            DateOnly effectiveFrom,
            HouseholdMember[] participants,
            HouseholdMember first,
            string? question = null,
            DateTimeOffset? created = null,
            TurnAdvanceMode advanceMode = TurnAdvanceMode.Daily)
        {
            var revision = new TurnRotationRevision(
                Guid.NewGuid(),
                rotationId,
                effectiveFrom,
                question,
                participants.Select(member => member.Id).ToArray(),
                first.Id,
                Adult.Id,
                created ?? Created,
                advanceMode);
            Revisions.Add(revision);
            return revision;
        }

        public TurnRotationRevision AddTicked(
            Guid rotationId,
            DateOnly effectiveFrom,
            HouseholdMember[] participants,
            HouseholdMember first) =>
            Add(rotationId, effectiveFrom, participants, first, advanceMode: TurnAdvanceMode.WhenTicked);

        /// <summary>Records a tick directly, as if it had been made on <paramref name="date"/>.</summary>
        public TakenTurn Tick(TurnRotationRevision revision, DateOnly date, HouseholdMember child)
        {
            var number = revision.GetTurn(date, date, TakenTurns)!.Value.Number;
            var taken = revision.TakeTurn(
                Guid.NewGuid(), Guid.NewGuid(), date, number, child.Id, TakenTurns, Adult.Id,
                Created.AddMinutes(TakenTurns.Count));
            TakenTurns.Add(taken);
            return taken;
        }

        public Task<IReadOnlyList<TurnRotationRevision>> GetRevisionsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TurnRotationRevision>>([.. Revisions]);

        public Task<IReadOnlyList<TakenTurn>> GetTakenTurnsAsync(
            IReadOnlyCollection<Guid> revisionIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TakenTurn>>(
                TakenTurns.Where(turn => revisionIds.Contains(turn.RevisionId) && !turn.IsUndone).ToArray());

        public Task<TakenTurn?> FindTakenTurnAsync(Guid takenTurnId, CancellationToken cancellationToken) =>
            Task.FromResult(TakenTurns.SingleOrDefault(turn => turn.Id == takenTurnId));

        public Task<TakenTurn?> GetTakenTurnByRequestAsync(Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult(TakenTurns.SingleOrDefault(turn => turn.RequestId == requestId));

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

        public Task<ITurnRotationLock> LockRotationAsync(Guid rotationId, CancellationToken cancellationToken)
        {
            var rotationLock = new FakeTurnRotationLock(rotationId);
            Locks.Add(rotationLock);
            return Task.FromResult<ITurnRotationLock>(rotationLock);
        }

        public Task AddRevisionAsync(TurnRotationRevision revision, CancellationToken cancellationToken)
        {
            Revisions.Add(revision);
            return Task.CompletedTask;
        }

        public Task AddTakenTurnAsync(TakenTurn takenTurn, CancellationToken cancellationToken)
        {
            TakenTurns.Add(takenTurn);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            foreach (var rotationLock in Locks.Where(item => !item.Disposed))
            {
                rotationLock.SavedWhileHeld = true;
            }

            return Task.CompletedTask;
        }
    }
}
