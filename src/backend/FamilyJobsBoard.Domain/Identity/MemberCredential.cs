namespace FamilyJobsBoard.Domain.Identity;

public sealed class MemberCredential
{
    private MemberCredential()
    {
    }

    public MemberCredential(Guid memberId)
    {
        if (memberId == Guid.Empty)
        {
            throw new ArgumentException("A credential needs a member.", nameof(memberId));
        }

        MemberId = memberId;
        State = CredentialState.NotSet;
    }

    public Guid MemberId { get; private set; }

    public CredentialState State { get; private set; }

    public string? PinHash { get; private set; }

    public DateTimeOffset? PinSetAtUtc { get; private set; }

    public DateTimeOffset? FailedWindowStartedAtUtc { get; private set; }

    public int FailedAttemptCount { get; private set; }

    public DateTimeOffset? LockedUntilUtc { get; private set; }

    public void SetPin(string pinHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pinHash);
        PinHash = pinHash;
        PinSetAtUtc = now;
        State = CredentialState.Ready;
        ResetFailures();
    }

    public void RecordFailure(DateTimeOffset now)
    {
        if (FailedWindowStartedAtUtc is null || now - FailedWindowStartedAtUtc >= TimeSpan.FromMinutes(5))
        {
            FailedWindowStartedAtUtc = now;
            FailedAttemptCount = 0;
        }

        FailedAttemptCount++;
        if (FailedAttemptCount >= 5)
        {
            LockedUntilUtc = now.AddMinutes(1);
        }
    }

    public void ResetFailures()
    {
        FailedWindowStartedAtUtc = null;
        FailedAttemptCount = 0;
        LockedUntilUtc = null;
    }
}
