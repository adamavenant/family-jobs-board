using FamilyJobsBoard.Domain.Settings;
using Xunit;

namespace FamilyJobsBoard.Domain.Tests;

public sealed class HouseholdSettingsTests
{
    private static readonly Guid Adult = Guid.Parse("d55b77d7-a514-4071-a004-f5033aff2f9d");
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 8, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void New_household_settings_give_thirty_seconds_to_undo()
    {
        var settings = HouseholdSettings.CreateDefault();

        Assert.Equal(HouseholdSettings.SingletonId, settings.Id);
        Assert.Equal(30, settings.UndoGracePeriodSeconds);
        Assert.Null(settings.UpdatedAtUtc);
        Assert.Null(settings.UpdatedByMemberId);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(120)]
    public void Undo_time_can_change_within_five_to_one_hundred_twenty_seconds(int seconds)
    {
        var settings = HouseholdSettings.CreateDefault();

        settings.ChangeUndoGracePeriod(seconds, Adult, Now);

        Assert.Equal(seconds, settings.UndoGracePeriodSeconds);
        Assert.Equal(Adult, settings.UpdatedByMemberId);
        Assert.Equal(TimeSpan.Zero, settings.UpdatedAtUtc!.Value.Offset);
        Assert.Equal(Now.ToUniversalTime(), settings.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(121)]
    [InlineData(int.MaxValue)]
    public void Undo_time_outside_the_limits_is_rejected(int seconds)
    {
        var settings = HouseholdSettings.CreateDefault();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            settings.ChangeUndoGracePeriod(seconds, Adult, Now));
        Assert.Equal(30, settings.UndoGracePeriodSeconds);
    }

    [Fact]
    public void A_settings_change_records_who_made_it()
    {
        var settings = HouseholdSettings.CreateDefault();

        Assert.Throws<ArgumentException>(() =>
            settings.ChangeUndoGracePeriod(45, Guid.Empty, Now));
    }
}
