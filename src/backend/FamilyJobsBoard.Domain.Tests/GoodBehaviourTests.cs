using FamilyJobsBoard.Domain.GoodBehaviours;
using FamilyJobsBoard.Domain.Points;
using Xunit;

namespace FamilyJobsBoard.Domain.Tests;

public sealed class GoodBehaviourTests
{
    private static readonly Guid Adult = Guid.Parse("d55b77d7-a514-4071-a004-f5033aff2f9d");
    private static readonly Guid OtherAdult = Guid.Parse("9db319c1-28d1-4ce6-93d7-f04a45f8257d");
    private static readonly Guid Child = Guid.Parse("96e7d927-dfb6-480c-89be-a5c58144f603");
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_type_is_active_trimmed_and_audited()
    {
        var type = NewType();

        Assert.True(type.IsActive);
        Assert.Equal("Being Brave", type.Name);
        Assert.Equal("Tried something hard.", type.Description);
        Assert.Equal(10, type.Points);
        Assert.Equal(Adult, type.CreatedByMemberId);
        Assert.Equal(Now, type.CreatedAtUtc);
    }

    [Theory]
    [InlineData("", 10)]
    [InlineData("   ", 10)]
    [InlineData("Being Brave", -1)]
    public void Type_rejects_missing_name_or_negative_points(string name, int points)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new GoodBehaviourType(Guid.NewGuid(), name, string.Empty, points, Adult, Now));
    }

    [Fact]
    public void Type_rejects_over_long_name_and_description()
    {
        Assert.Throws<ArgumentException>(() => new GoodBehaviourType(
            Guid.NewGuid(),
            new string('a', GoodBehaviourType.MaximumNameLength + 1),
            string.Empty,
            1,
            Adult,
            Now));
        Assert.Throws<ArgumentException>(() => new GoodBehaviourType(
            Guid.NewGuid(),
            "Name",
            new string('a', GoodBehaviourType.MaximumDescriptionLength + 1),
            1,
            Adult,
            Now));
    }

    [Fact]
    public void Update_changes_details_and_records_who_and_when()
    {
        var type = NewType();

        type.Update("Being Kind", "Kind act.", 7, OtherAdult, Now.AddHours(1));

        Assert.Equal("Being Kind", type.Name);
        Assert.Equal("Kind act.", type.Description);
        Assert.Equal(7, type.Points);
        Assert.Equal(OtherAdult, type.UpdatedByMemberId);
        Assert.Equal(Now.AddHours(1), type.UpdatedAtUtc);
    }

    [Fact]
    public void Deactivate_is_a_soft_delete_and_is_idempotent()
    {
        var type = NewType();

        type.Deactivate(OtherAdult, Now.AddHours(1));
        type.Deactivate(Adult, Now.AddHours(2));

        Assert.False(type.IsActive);
        Assert.Equal(OtherAdult, type.DeactivatedByMemberId);
        Assert.Equal(Now.AddHours(1), type.DeactivatedAtUtc);
    }

    [Fact]
    public void Deleted_type_cannot_be_edited()
    {
        var type = NewType();
        type.Deactivate(Adult, Now);

        Assert.Throws<GoodBehaviourTypeInactiveException>(() =>
            type.Update("Renamed", string.Empty, 1, Adult, Now));
    }

    [Fact]
    public void Logged_behaviour_snapshots_the_type_so_later_edits_do_not_change_it()
    {
        var type = NewType();
        var behaviour = new GoodBehaviour(
            Guid.NewGuid(), Guid.NewGuid(), type, Child, Adult, 12, Now);

        type.Update("Renamed", "Different.", 1, Adult, Now.AddDays(1));
        type.Deactivate(Adult, Now.AddDays(2));

        Assert.Equal("Being Brave", behaviour.TypeName);
        Assert.Equal("Tried something hard.", behaviour.TypeDescription);
        Assert.Equal(12, behaviour.Points);
        Assert.Equal(type.Id, behaviour.TypeId);
        Assert.Equal(Adult, behaviour.LoggedByMemberId);
    }

    [Fact]
    public void Behaviour_rejects_missing_identifiers_and_negative_points()
    {
        var type = NewType();

        Assert.Throws<ArgumentException>(() => new GoodBehaviour(
            Guid.NewGuid(), Guid.Empty, type, Child, Adult, 1, Now));
        Assert.Throws<ArgumentException>(() => new GoodBehaviour(
            Guid.NewGuid(), Guid.NewGuid(), type, Guid.Empty, Adult, 1, Now));
        Assert.Throws<ArgumentException>(() => new GoodBehaviour(
            Guid.NewGuid(), Guid.NewGuid(), type, Child, Guid.Empty, 1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GoodBehaviour(
            Guid.NewGuid(), Guid.NewGuid(), type, Child, Adult, -1, Now));
    }

    [Fact]
    public void Good_behaviour_ledger_entry_has_exactly_one_source()
    {
        var behaviourId = Guid.NewGuid();

        var entry = PointsLedgerEntry.ForGoodBehaviour(
            Guid.NewGuid(), Child, behaviourId, 5, Now);

        Assert.Equal(behaviourId, entry.GoodBehaviourId);
        Assert.Null(entry.JobId);
        Assert.Equal(5, entry.Amount);
        Assert.Throws<ArgumentException>(() => PointsLedgerEntry.ForGoodBehaviour(
            Guid.NewGuid(), Child, Guid.Empty, 5, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => PointsLedgerEntry.ForGoodBehaviour(
            Guid.NewGuid(), Child, behaviourId, -1, Now));
    }

    private static GoodBehaviourType NewType() =>
        new(Guid.NewGuid(), "  Being Brave ", " Tried something hard. ", 10, Adult, Now);
}
