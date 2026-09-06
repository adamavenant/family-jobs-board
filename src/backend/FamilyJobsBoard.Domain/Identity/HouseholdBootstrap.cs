namespace FamilyJobsBoard.Domain.Identity;

public sealed class HouseholdBootstrap
{
    public const short SingletonId = 1;

    private HouseholdBootstrap()
    {
    }

    public HouseholdBootstrap(short id)
    {
        if (id != SingletonId)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        Id = id;
        State = BootstrapState.Required;
    }

    public short Id { get; private set; }

    public BootstrapState State { get; private set; }

    public Guid? FirstAdultId { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public void Complete(Guid adultId, DateTimeOffset completedAtUtc)
    {
        if (State == BootstrapState.Complete)
        {
            throw new InvalidOperationException("The household is already bootstrapped.");
        }

        if (adultId == Guid.Empty)
        {
            throw new ArgumentException("A first adult is required.", nameof(adultId));
        }

        State = BootstrapState.Complete;
        FirstAdultId = adultId;
        CompletedAtUtc = completedAtUtc;
    }
}

public enum BootstrapState
{
    Required,
    Complete,
}
