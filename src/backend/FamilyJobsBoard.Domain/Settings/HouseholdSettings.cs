namespace FamilyJobsBoard.Domain.Settings;

/// <summary>
/// The household's shared preferences. There is one per household, so every device reads
/// the same values. A household that has never saved its settings uses the defaults.
/// </summary>
public sealed class HouseholdSettings
{
    public const int SingletonId = 1;
    public const int MinimumUndoGracePeriodSeconds = 5;
    public const int MaximumUndoGracePeriodSeconds = 120;
    public const int DefaultUndoGracePeriodSeconds = 30;

    private HouseholdSettings()
    {
    }

    private HouseholdSettings(int undoGracePeriodSeconds)
    {
        Id = SingletonId;
        UndoGracePeriodSeconds = undoGracePeriodSeconds;
    }

    public int Id { get; private set; }

    /// <summary>
    /// How long, in whole seconds, an action waits with an Undo toast before it is saved.
    /// </summary>
    public int UndoGracePeriodSeconds { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedByMemberId { get; private set; }

    public static HouseholdSettings CreateDefault() => new(DefaultUndoGracePeriodSeconds);

    public static bool IsValidUndoGracePeriod(int seconds) =>
        seconds is >= MinimumUndoGracePeriodSeconds and <= MaximumUndoGracePeriodSeconds;

    public void ChangeUndoGracePeriod(int seconds, Guid actorMemberId, DateTimeOffset now)
    {
        if (!IsValidUndoGracePeriod(seconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(seconds),
                $"The undo time must be {MinimumUndoGracePeriodSeconds} to {MaximumUndoGracePeriodSeconds} seconds.");
        }

        if (actorMemberId == Guid.Empty)
        {
            throw new ArgumentException("A settings change needs an actor ID.", nameof(actorMemberId));
        }

        UndoGracePeriodSeconds = seconds;
        UpdatedAtUtc = now.ToUniversalTime();
        UpdatedByMemberId = actorMemberId;
    }
}
