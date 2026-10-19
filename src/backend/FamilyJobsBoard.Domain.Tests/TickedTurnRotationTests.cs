using FamilyJobsBoard.Domain.TurnRotations;
using Xunit;

namespace FamilyJobsBoard.Domain.Tests;

public sealed class TickedTurnRotationTests
{
    private static readonly Guid Adult = Guid.Parse("d55b77d7-a514-4071-a004-f5033aff2f9d");
    private static readonly Guid ChildA = Guid.Parse("96e7d927-dfb6-480c-89be-a5c58144f603");
    private static readonly Guid ChildB = Guid.Parse("3fdb1469-df09-420e-8eeb-330717c710fe");
    private static readonly Guid ChildC = Guid.Parse("9db319c1-28d1-4ce6-93d7-f04a45f8257d");
    private static readonly DateOnly Monday = new(2026, 9, 21);
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Three_child_rotation_stays_put_until_a_turn_is_ticked_then_moves_on_the_next_day()
    {
        var revision = Ticked([ChildA, ChildB, ChildC], ChildA);
        List<TakenTurn> taken = [];

        Assert.Equal(new TurnRotationTurn(ChildA, false, 0), revision.GetTurn(Monday, Monday, taken));
        Assert.Equal(new TurnRotationTurn(ChildA, false, 0), revision.GetTurn(Monday.AddDays(3), Monday, taken));

        taken.Add(Tick(revision, Monday, ChildA, taken));

        Assert.Equal(new TurnRotationTurn(ChildA, true, 0), revision.GetTurn(Monday, Monday, taken));
        var tuesday = Monday.AddDays(1);
        Assert.Equal(new TurnRotationTurn(ChildB, false, 1), revision.GetTurn(tuesday, tuesday, taken));
        var wednesday = Monday.AddDays(2);
        Assert.Equal(new TurnRotationTurn(ChildB, false, 1), revision.GetTurn(wednesday, wednesday, taken));
        Assert.Equal(new TurnRotationTurn(ChildA, true, 0), revision.GetTurn(Monday, wednesday, taken));
    }

    [Fact]
    public void Three_child_rotation_can_be_ticked_at_most_once_a_day()
    {
        var revision = Ticked([ChildA, ChildB, ChildC], ChildA);
        List<TakenTurn> taken = [Tick(revision, Monday, ChildA, [])];

        Assert.Throws<InvalidOperationException>(
            () => revision.TakeTurn(Guid.NewGuid(), Guid.NewGuid(), Monday, 0, ChildA, taken, Adult, Now));
        Assert.Throws<InvalidOperationException>(
            () => revision.TakeTurn(Guid.NewGuid(), Guid.NewGuid(), Monday, 1, ChildB, taken, Adult, Now));
    }

    [Fact]
    public void Two_child_rotation_moves_on_as_soon_as_a_turn_is_ticked()
    {
        var revision = Ticked([ChildA, ChildB], ChildA);
        List<TakenTurn> taken = [];
        Assert.True(revision.MovesOnImmediately);

        taken.Add(Tick(revision, Monday, ChildA, taken));
        Assert.Equal(new TurnRotationTurn(ChildB, false, 1), revision.GetTurn(Monday, Monday, taken));

        taken.Add(Tick(revision, Monday, ChildB, taken));
        Assert.Equal(new TurnRotationTurn(ChildA, false, 2), revision.GetTurn(Monday, Monday, taken));
        Assert.Equal([0, 1], taken.Select(turn => turn.TurnNumber));
    }

    [Fact]
    public void Two_child_rotation_shows_a_past_day_as_whose_turn_it_was_that_morning()
    {
        var revision = Ticked([ChildA, ChildB], ChildA);
        TakenTurn[] taken = [Tick(revision, Monday, ChildA, [])];
        var tuesday = Monday.AddDays(1);

        Assert.Equal(new TurnRotationTurn(ChildA, true, 0), revision.GetTurn(Monday, tuesday, taken));
        Assert.Equal(new TurnRotationTurn(ChildB, false, 1), revision.GetTurn(tuesday, tuesday, taken));
    }

    [Fact]
    public void One_child_rotation_marks_today_done_and_starts_afresh_tomorrow()
    {
        var revision = Ticked([ChildA], ChildA);
        TakenTurn[] taken = [Tick(revision, Monday, ChildA, [])];
        var tuesday = Monday.AddDays(1);

        Assert.False(revision.MovesOnImmediately);
        Assert.Equal(new TurnRotationTurn(ChildA, true, 0), revision.GetTurn(Monday, Monday, taken));
        Assert.Equal(new TurnRotationTurn(ChildA, false, 1), revision.GetTurn(tuesday, tuesday, taken));
    }

    [Fact]
    public void Future_dates_show_the_child_whose_turn_it_is_now()
    {
        var revision = Ticked([ChildA, ChildB, ChildC], ChildB);
        TakenTurn[] taken = [Tick(revision, Monday, ChildB, [])];

        foreach (var offset in new[] { 1, 2, 7 })
        {
            Assert.Equal(
                new TurnRotationTurn(ChildC, false, 1),
                revision.GetTurn(Monday.AddDays(offset), Monday, taken));
        }
    }

    [Fact]
    public void A_tick_must_name_the_child_whose_turn_it_is()
    {
        var revision = Ticked([ChildA, ChildB], ChildA);
        TakenTurn[] taken = [Tick(revision, Monday, ChildA, [])];

        Assert.Throws<InvalidOperationException>(
            () => revision.TakeTurn(Guid.NewGuid(), Guid.NewGuid(), Monday, 1, ChildA, taken, Adult, Now));
    }

    [Fact]
    public void A_stale_tick_for_a_childs_earlier_turn_is_refused_when_their_turn_comes_round_again()
    {
        var revision = Ticked([ChildA, ChildB], ChildA);
        List<TakenTurn> taken = [];
        taken.Add(revision.TakeTurn(Guid.NewGuid(), Guid.NewGuid(), Monday, 0, ChildA, taken, Adult, Now));
        taken.Add(revision.TakeTurn(Guid.NewGuid(), Guid.NewGuid(), Monday, 1, ChildB, taken, Adult, Now));

        Assert.Equal(new TurnRotationTurn(ChildA, false, 2), revision.GetTurn(Monday, Monday, taken));
        Assert.Throws<InvalidOperationException>(
            () => revision.TakeTurn(Guid.NewGuid(), Guid.NewGuid(), Monday, 0, ChildA, taken, Adult, Now));
        Assert.Equal(2, taken.Count);
    }

    [Fact]
    public void A_daily_turn_number_counts_days_since_the_effective_date()
    {
        var daily = new TurnRotationRevision(
            Guid.NewGuid(), Guid.NewGuid(), Monday, null, [ChildA, ChildB], ChildB, Adult, Now);

        Assert.Equal(new TurnRotationTurn(ChildB, false, 0), daily.GetTurn(Monday, Monday, []));
        Assert.Equal(new TurnRotationTurn(ChildB, false, 4), daily.GetTurn(Monday.AddDays(4), Monday, []));
    }

    [Fact]
    public void An_unticked_turn_no_longer_counts_and_cannot_be_unticked_twice()
    {
        var revision = Ticked([ChildA, ChildB, ChildC], ChildA);
        var tick = Tick(revision, Monday, ChildA, []);

        tick.Undo(ChildA, Now.AddMinutes(5));

        Assert.True(tick.IsUndone);
        Assert.Equal(ChildA, tick.UndoneByMemberId);
        Assert.Equal(new TurnRotationTurn(ChildA, false, 0), revision.GetTurn(Monday, Monday, [tick]));
        Assert.Null(revision.LatestTakenTurn([tick]));
        Assert.Throws<InvalidOperationException>(() => tick.Undo(Adult, Now.AddMinutes(6)));
    }

    [Fact]
    public void Latest_taken_turn_is_the_most_recent_tick_that_still_counts()
    {
        var revision = Ticked([ChildA, ChildB], ChildA);
        var first = Tick(revision, Monday, ChildA, []);
        var second = revision.TakeTurn(
            Guid.NewGuid(), Guid.NewGuid(), Monday, 1, ChildB, [first], Adult, Now.AddMinutes(1));

        Assert.Same(second, revision.LatestTakenTurn([first, second]));
        second.Undo(Adult, Now.AddMinutes(2));
        Assert.Same(first, revision.LatestTakenTurn([first, second]));
    }

    [Fact]
    public void Ticks_from_another_revision_do_not_count()
    {
        var earlier = Ticked([ChildA, ChildB, ChildC], ChildA);
        TakenTurn[] taken = [Tick(earlier, Monday, ChildA, [])];
        var tuesday = Monday.AddDays(1);
        var later = new TurnRotationRevision(
            Guid.NewGuid(), earlier.RotationId, tuesday, null, [ChildA, ChildB, ChildC], ChildA, Adult, Now,
            TurnAdvanceMode.WhenTicked);

        Assert.Equal(new TurnRotationTurn(ChildA, false, 0), later.GetTurn(tuesday, tuesday, taken));
    }

    [Fact]
    public void A_daily_rotation_ignores_ticks_and_cannot_be_ticked()
    {
        var daily = new TurnRotationRevision(
            Guid.NewGuid(), Guid.NewGuid(), Monday, null, [ChildA, ChildB], ChildA, Adult, Now);
        var ticked = Ticked([ChildA, ChildB], ChildA);

        Assert.Equal(TurnAdvanceMode.Daily, daily.AdvanceMode);
        Assert.False(daily.MovesOnImmediately);
        Assert.Equal(new TurnRotationTurn(ChildB, false, 1), daily.GetTurn(Monday.AddDays(1), Monday, []));
        Assert.Throws<InvalidOperationException>(
            () => daily.TakeTurn(Guid.NewGuid(), Guid.NewGuid(), Monday, 0, ChildA, [], Adult, Now));
        Assert.Throws<InvalidOperationException>(() => ticked.GetAssignedChildId(Monday));
    }

    [Fact]
    public void Rejects_an_unknown_advance_mode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TurnRotationRevision(
            Guid.NewGuid(), Guid.NewGuid(), Monday, null, [ChildA], ChildA, Adult, Now, (TurnAdvanceMode)42));
    }

    private static TurnRotationRevision Ticked(Guid[] participants, Guid first) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Monday, null, participants, first, Adult, Now, TurnAdvanceMode.WhenTicked);

    /// <summary>Ticks off the current turn, naming it by its number as a client would.</summary>
    private static TakenTurn Tick(
        TurnRotationRevision revision,
        DateOnly today,
        Guid childId,
        IReadOnlyCollection<TakenTurn> taken)
    {
        var number = revision.GetTurn(today, today, taken)!.Value.Number;
        return revision.TakeTurn(
            Guid.NewGuid(), Guid.NewGuid(), today, number, childId, taken, Adult, Now.AddMinutes(taken.Count));
    }
}
