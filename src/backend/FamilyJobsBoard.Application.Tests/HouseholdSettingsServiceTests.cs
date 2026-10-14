using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Application.Settings;
using FamilyJobsBoard.Domain.Settings;
using Xunit;

namespace FamilyJobsBoard.Application.Tests;

public sealed class HouseholdSettingsServiceTests
{
    private static readonly Guid Adult = Guid.Parse("d55b77d7-a514-4071-a004-f5033aff2f9d");
    private static readonly Guid OtherAdult = Guid.Parse("0d3c6f1c-8d43-4a0e-9a64-2f6f6ad2bb19");

    [Fact]
    public async Task A_household_that_never_saved_settings_uses_thirty_seconds()
    {
        var repository = new InMemoryHouseholdSettingsRepository();

        var settings = await new HouseholdSettingsService(repository, new FixedClock())
            .GetAsync(CancellationToken.None);

        Assert.Equal(new HouseholdSettingsView(30, 5, 120), settings);
        Assert.Null(repository.Saved);
    }

    [Fact]
    public async Task First_change_creates_the_settings_with_who_changed_them_and_when()
    {
        var repository = new InMemoryHouseholdSettingsRepository();
        var service = new HouseholdSettingsService(repository, new FixedClock());

        var updated = await service.UpdateAsync(
            Adult,
            new UpdateHouseholdSettings(45),
            CancellationToken.None);

        Assert.Equal(45, updated.UndoGracePeriodSeconds);
        Assert.Equal(45, (await service.GetAsync(CancellationToken.None)).UndoGracePeriodSeconds);
        Assert.Equal(Adult, repository.Saved!.UpdatedByMemberId);
        Assert.Equal(FixedClock.Now, repository.Saved.UpdatedAtUtc);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Later_changes_update_the_same_settings()
    {
        var repository = new InMemoryHouseholdSettingsRepository();
        var service = new HouseholdSettingsService(repository, new FixedClock());
        await service.UpdateAsync(Adult, new UpdateHouseholdSettings(45), CancellationToken.None);
        var first = repository.Saved;

        await service.UpdateAsync(OtherAdult, new UpdateHouseholdSettings(5), CancellationToken.None);

        Assert.Same(first, repository.Saved);
        Assert.Equal(5, repository.Saved!.UndoGracePeriodSeconds);
        Assert.Equal(OtherAdult, repository.Saved.UpdatedByMemberId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(4)]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(121)]
    public async Task Undo_time_outside_five_to_one_hundred_twenty_seconds_is_rejected(int? seconds)
    {
        var repository = new InMemoryHouseholdSettingsRepository();
        var service = new HouseholdSettingsService(repository, new FixedClock());

        var exception = await Assert.ThrowsAsync<InvalidHouseholdSettingsException>(() =>
            service.UpdateAsync(Adult, new UpdateHouseholdSettings(seconds), CancellationToken.None));

        Assert.Contains(nameof(UpdateHouseholdSettings.UndoGracePeriodSeconds), exception.Errors.Keys);
        Assert.Null(repository.Saved);
        Assert.Equal(0, repository.SaveCount);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(120)]
    public async Task Undo_time_at_the_limits_is_accepted(int seconds)
    {
        var repository = new InMemoryHouseholdSettingsRepository();

        var updated = await new HouseholdSettingsService(repository, new FixedClock())
            .UpdateAsync(Adult, new UpdateHouseholdSettings(seconds), CancellationToken.None);

        Assert.Equal(seconds, updated.UndoGracePeriodSeconds);
    }

    [Fact]
    public async Task A_concurrent_first_save_is_retried_as_an_update()
    {
        var concurrent = HouseholdSettings.CreateDefault();
        concurrent.ChangeUndoGracePeriod(60, OtherAdult, FixedClock.Now);
        var repository = new InMemoryHouseholdSettingsRepository { ConcurrentInsert = concurrent };

        var updated = await new HouseholdSettingsService(repository, new FixedClock())
            .UpdateAsync(Adult, new UpdateHouseholdSettings(15), CancellationToken.None);

        Assert.Equal(15, updated.UndoGracePeriodSeconds);
        Assert.Same(concurrent, repository.Saved);
        Assert.Equal(Adult, repository.Saved!.UpdatedByMemberId);
    }

    private sealed class FixedClock : IHouseholdClock
    {
        public static readonly DateTimeOffset Now = new(2026, 10, 14, 6, 0, 0, TimeSpan.Zero);

        public DateOnly Today => new(2026, 10, 14);

        public DateTimeOffset UtcNow => Now;
    }
}
