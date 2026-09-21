using FamilyJobsBoard.Domain.PointAdjustments;
using FamilyJobsBoard.Domain.Points;
using Xunit;

namespace FamilyJobsBoard.Domain.Tests;

public sealed class PointAdjustmentTests
{
    private static readonly Guid Adult = Guid.Parse("d55b77d7-a514-4071-a004-f5033aff2f9d");
    private static readonly Guid Child = Guid.Parse("96e7d927-dfb6-480c-89be-a5c58144f603");
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 8, 0, 0, TimeSpan.FromHours(2));

    [Theory]
    [InlineData(5)]
    [InlineData(-5)]
    public void Adjustment_may_add_or_remove_points_and_records_who_and_when(int amount)
    {
        var adjustment = New(amount, "  Bonus for helping out  ");

        Assert.Equal(amount, adjustment.Amount);
        Assert.Equal("Bonus for helping out", adjustment.Reason);
        Assert.Equal(Adult, adjustment.AdjustedByMemberId);
        Assert.Equal(Child, adjustment.ChildId);
        Assert.Equal(TimeSpan.Zero, adjustment.AdjustedAtUtc.Offset);
        Assert.Equal(Now.ToUniversalTime(), adjustment.AdjustedAtUtc);
    }

    [Fact]
    public void Adjustment_must_change_the_balance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => New(0, "Reason"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Adjustment_requires_a_reason(string? reason)
    {
        Assert.Throws<ArgumentException>(() => New(3, reason!));
    }

    [Fact]
    public void Reason_is_limited_in_length()
    {
        var atLimit = new string('a', PointAdjustment.MaximumReasonLength);

        Assert.Equal(atLimit, New(1, atLimit).Reason);
        Assert.Throws<ArgumentException>(() => New(1, atLimit + "a"));
    }

    [Fact]
    public void Adjustment_requires_identifiers()
    {
        Assert.Throws<ArgumentException>(() => new PointAdjustment(
            Guid.Empty, Guid.NewGuid(), Child, Adult, 1, "Reason", Now));
        Assert.Throws<ArgumentException>(() => new PointAdjustment(
            Guid.NewGuid(), Guid.Empty, Child, Adult, 1, "Reason", Now));
        Assert.Throws<ArgumentException>(() => new PointAdjustment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Adult, 1, "Reason", Now));
        Assert.Throws<ArgumentException>(() => new PointAdjustment(
            Guid.NewGuid(), Guid.NewGuid(), Child, Guid.Empty, 1, "Reason", Now));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(-7)]
    public void Manual_adjustment_ledger_entry_carries_the_signed_amount_and_one_source(int amount)
    {
        var adjustmentId = Guid.NewGuid();

        var entry = PointsLedgerEntry.ForManualAdjustment(
            Guid.NewGuid(), Child, adjustmentId, amount, Now);

        Assert.Equal(amount, entry.Amount);
        Assert.Equal(adjustmentId, entry.PointAdjustmentId);
        Assert.Null(entry.JobId);
        Assert.Null(entry.GoodBehaviourId);
    }

    [Fact]
    public void Manual_adjustment_ledger_entry_rejects_zero_and_missing_source()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PointsLedgerEntry.ForManualAdjustment(
            Guid.NewGuid(), Child, Guid.NewGuid(), 0, Now));
        Assert.Throws<ArgumentException>(() => PointsLedgerEntry.ForManualAdjustment(
            Guid.NewGuid(), Child, Guid.Empty, 1, Now));
    }

    [Fact]
    public void Only_manual_adjustments_may_be_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PointsLedgerEntry(
            Guid.NewGuid(), Child, Guid.NewGuid(), -1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => PointsLedgerEntry.ForGoodBehaviour(
            Guid.NewGuid(), Child, Guid.NewGuid(), -1, Now));
    }

    private static PointAdjustment New(int amount, string reason) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Child, Adult, amount, reason, Now);
}
