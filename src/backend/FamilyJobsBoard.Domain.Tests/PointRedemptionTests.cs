using FamilyJobsBoard.Domain.PointRedemptions;
using FamilyJobsBoard.Domain.Points;
using Xunit;

namespace FamilyJobsBoard.Domain.Tests;

public sealed class PointRedemptionTests
{
    private static readonly Guid Adult = Guid.Parse("d55b77d7-a514-4071-a004-f5033aff2f9d");
    private static readonly Guid Child = Guid.Parse("96e7d927-dfb6-480c-89be-a5c58144f603");
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 10, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Redemption_records_the_points_reward_redeeming_adult_and_utc_time()
    {
        var redemption = New(12, "  Movie night  ");

        Assert.Equal(12, redemption.Points);
        Assert.Equal("Movie night", redemption.Reward);
        Assert.Equal(Adult, redemption.RedeemedByMemberId);
        Assert.Equal(Child, redemption.ChildId);
        Assert.Equal(TimeSpan.Zero, redemption.RedeemedAtUtc.Offset);
        Assert.Equal(Now, redemption.RedeemedAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Redemption_must_spend_at_least_one_point(int points)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => New(points, "Ice cream"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Redemption_needs_a_reward(string? reward)
    {
        Assert.Throws<ArgumentException>(() => New(1, reward!));
    }

    [Fact]
    public void Reward_is_limited_after_trimming()
    {
        var longest = new string('a', PointRedemption.MaximumRewardLength);

        Assert.Equal(longest, New(1, $"  {longest}  ").Reward);
        Assert.Throws<ArgumentException>(() => New(1, longest + "a"));
    }

    [Fact]
    public void Redemption_needs_ids_child_and_redeeming_adult()
    {
        Assert.Throws<ArgumentException>(() => new PointRedemption(
            Guid.Empty, Guid.NewGuid(), Child, Adult, 1, "Ice cream", Now));
        Assert.Throws<ArgumentException>(() => new PointRedemption(
            Guid.NewGuid(), Guid.Empty, Child, Adult, 1, "Ice cream", Now));
        Assert.Throws<ArgumentException>(() => new PointRedemption(
            Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Adult, 1, "Ice cream", Now));
        Assert.Throws<ArgumentException>(() => new PointRedemption(
            Guid.NewGuid(), Guid.NewGuid(), Child, Guid.Empty, 1, "Ice cream", Now));
    }

    [Fact]
    public void Redemption_ledger_entry_is_negative_with_one_source()
    {
        var redemptionId = Guid.NewGuid();

        var entry = PointsLedgerEntry.ForRedemption(Guid.NewGuid(), Child, redemptionId, 9, Now);

        Assert.Equal(-9, entry.Amount);
        Assert.Equal(redemptionId, entry.PointRedemptionId);
        Assert.Equal(Child, entry.ChildId);
        Assert.Null(entry.JobId);
        Assert.Null(entry.GoodBehaviourId);
        Assert.Null(entry.PointAdjustmentId);
    }

    [Fact]
    public void Redemption_ledger_entry_rejects_non_positive_points_and_missing_source()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PointsLedgerEntry.ForRedemption(
            Guid.NewGuid(), Child, Guid.NewGuid(), 0, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => PointsLedgerEntry.ForRedemption(
            Guid.NewGuid(), Child, Guid.NewGuid(), -4, Now));
        Assert.Throws<ArgumentException>(() => PointsLedgerEntry.ForRedemption(
            Guid.NewGuid(), Child, Guid.Empty, 1, Now));
    }

    private static PointRedemption New(int points, string reward) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Child, Adult, points, reward, Now);
}
