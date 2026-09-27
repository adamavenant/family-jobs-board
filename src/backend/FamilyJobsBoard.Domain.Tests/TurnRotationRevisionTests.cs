using FamilyJobsBoard.Domain.TurnRotations;
using Xunit;

namespace FamilyJobsBoard.Domain.Tests;

public sealed class TurnRotationRevisionTests
{
    private static readonly Guid Adult = Guid.Parse("d55b77d7-a514-4071-a004-f5033aff2f9d");
    private static readonly Guid ChildA = Guid.Parse("96e7d927-dfb6-480c-89be-a5c58144f603");
    private static readonly Guid ChildB = Guid.Parse("3fdb1469-df09-420e-8eeb-330717c710fe");
    private static readonly Guid ChildC = Guid.Parse("9db319c1-28d1-4ce6-93d7-f04a45f8257d");
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Three_child_rotation_cycles_in_order_starting_from_the_first_child()
    {
        var monday = new DateOnly(2026, 9, 21);
        var revision = new TurnRotationRevision(
            Guid.NewGuid(), Guid.NewGuid(), monday, null, [ChildA, ChildB, ChildC], ChildA, Adult, Now);

        Assert.Equal((Guid?)ChildA, revision.GetAssignedChildId(monday));
        Assert.Equal((Guid?)ChildB, revision.GetAssignedChildId(monday.AddDays(1)));
        Assert.Equal((Guid?)ChildC, revision.GetAssignedChildId(monday.AddDays(2)));
        Assert.Equal((Guid?)ChildA, revision.GetAssignedChildId(monday.AddDays(3)));
    }

    [Fact]
    public void One_child_rotation_always_answers_with_that_child()
    {
        var effectiveFrom = new DateOnly(2026, 9, 21);
        var revision = new TurnRotationRevision(
            Guid.NewGuid(), Guid.NewGuid(), effectiveFrom, null, [ChildA], ChildA, Adult, Now);

        for (var offset = 0; offset < 30; offset++)
        {
            Assert.Equal((Guid?)ChildA, revision.GetAssignedChildId(effectiveFrom.AddDays(offset)));
        }
    }

    [Fact]
    public void First_child_sets_the_starting_offset_without_reordering_the_others()
    {
        var effectiveFrom = new DateOnly(2026, 9, 21);
        var revision = new TurnRotationRevision(
            Guid.NewGuid(), Guid.NewGuid(), effectiveFrom, null, [ChildA, ChildB, ChildC], ChildB, Adult, Now);

        Assert.Equal((Guid?)ChildB, revision.GetAssignedChildId(effectiveFrom));
        Assert.Equal((Guid?)ChildC, revision.GetAssignedChildId(effectiveFrom.AddDays(1)));
        Assert.Equal((Guid?)ChildA, revision.GetAssignedChildId(effectiveFrom.AddDays(2)));
        Assert.Equal((Guid?)ChildB, revision.GetAssignedChildId(effectiveFrom.AddDays(3)));
    }

    [Fact]
    public void Rotation_arithmetic_is_stable_across_a_leap_day()
    {
        var effectiveFrom = new DateOnly(2028, 2, 27);
        var revision = new TurnRotationRevision(
            Guid.NewGuid(), Guid.NewGuid(), effectiveFrom, null, [ChildA, ChildB], ChildA, Adult, Now);

        Assert.Equal((Guid?)ChildA, revision.GetAssignedChildId(new DateOnly(2028, 2, 27)));
        Assert.Equal((Guid?)ChildB, revision.GetAssignedChildId(new DateOnly(2028, 2, 28)));
        Assert.Equal((Guid?)ChildA, revision.GetAssignedChildId(new DateOnly(2028, 2, 29)));
        Assert.Equal((Guid?)ChildB, revision.GetAssignedChildId(new DateOnly(2028, 3, 1)));
    }

    [Fact]
    public void Rotation_arithmetic_is_stable_across_a_daylight_saving_boundary()
    {
        // Household dates are plain calendar DateOnly values with no time-of-day
        // component, so a rotation never re-derives a wall-clock time from an
        // elapsed-day count and cannot be perturbed by a clock-change day, even
        // in a time zone (unlike the household's own Africa/Johannesburg) that
        // observes daylight saving.
        var effectiveFrom = new DateOnly(2026, 3, 6);
        var revision = new TurnRotationRevision(
            Guid.NewGuid(), Guid.NewGuid(), effectiveFrom, null, [ChildA, ChildB, ChildC], ChildA, Adult, Now);

        Assert.Equal((Guid?)ChildA, revision.GetAssignedChildId(new DateOnly(2026, 3, 6)));
        Assert.Equal((Guid?)ChildB, revision.GetAssignedChildId(new DateOnly(2026, 3, 7)));
        Assert.Equal((Guid?)ChildC, revision.GetAssignedChildId(new DateOnly(2026, 3, 8)));
        Assert.Equal((Guid?)ChildA, revision.GetAssignedChildId(new DateOnly(2026, 3, 9)));
    }

    [Fact]
    public void Default_question_applies_when_none_is_given()
    {
        var revision = new TurnRotationRevision(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 21), "   ", [ChildA], ChildA, Adult, Now);

        Assert.Equal(TurnRotationRevision.DefaultQuestion, revision.Question);
    }

    [Fact]
    public void Custom_question_is_trimmed()
    {
        var revision = new TurnRotationRevision(
            Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 9, 21),
            "  Who sets the table today?  ",
            [ChildA],
            ChildA,
            Adult,
            Now);

        Assert.Equal("Who sets the table today?", revision.Question);
    }

    [Fact]
    public void Rejects_a_date_before_the_effective_date()
    {
        var effectiveFrom = new DateOnly(2026, 9, 21);
        var revision = new TurnRotationRevision(
            Guid.NewGuid(), Guid.NewGuid(), effectiveFrom, null, [ChildA, ChildB], ChildA, Adult, Now);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => revision.GetAssignedChildId(effectiveFrom.AddDays(-1)));
    }

    [Fact]
    public void Rejects_no_participants()
    {
        Assert.Throws<ArgumentException>(() => new TurnRotationRevision(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 21), null, [], ChildA, Adult, Now));
    }

    [Fact]
    public void Rejects_duplicate_participants()
    {
        Assert.Throws<ArgumentException>(() => new TurnRotationRevision(
            Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 9, 21),
            null,
            [ChildA, ChildB, ChildA],
            ChildA,
            Adult,
            Now));
    }

    [Fact]
    public void Rejects_a_first_child_not_among_the_participants()
    {
        Assert.Throws<ArgumentException>(() => new TurnRotationRevision(
            Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 9, 21),
            null,
            [ChildA, ChildB],
            ChildC,
            Adult,
            Now));
    }

    [Fact]
    public void Rejects_an_over_long_question()
    {
        Assert.Throws<ArgumentException>(() => new TurnRotationRevision(
            Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 9, 21),
            new string('a', TurnRotationRevision.MaximumQuestionLength + 1),
            [ChildA],
            ChildA,
            Adult,
            Now));
    }

    [Fact]
    public void Participants_are_stored_in_the_given_order()
    {
        var revision = new TurnRotationRevision(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 21), null, [ChildC, ChildA, ChildB], ChildC, Adult, Now);

        Assert.Equal([ChildC, ChildA, ChildB], revision.OrderedParticipantChildIds);
    }
}
