using FamilyJobsBoard.Domain.Households;

namespace FamilyJobsBoard.Domain.Identity;

public sealed class AuthSession
{
    private AuthSession()
    {
    }

    public AuthSession(
        Guid id,
        Guid memberId,
        HouseholdRole role,
        string refreshTokenHash,
        DateTimeOffset now)
    {
        if (id == Guid.Empty || memberId == Guid.Empty)
        {
            throw new ArgumentException("A session needs IDs.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(refreshTokenHash);
        Id = id;
        MemberId = memberId;
        Role = role;
        RefreshTokenHash = refreshTokenHash;
        CreatedAtUtc = now;
        LastActivityAtUtc = now;
    }

    public Guid Id { get; private set; }
    public Guid MemberId { get; private set; }
    public HouseholdRole Role { get; private set; }
    public string RefreshTokenHash { get; private set; } = string.Empty;
    public int RotationVersion { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset LastActivityAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool IsActive(DateTimeOffset now) =>
        RevokedAtUtc is null && now - LastActivityAtUtc < TimeSpan.FromMinutes(10);

    public void Touch(DateTimeOffset now)
    {
        if (!IsActive(now))
        {
            throw new InvalidOperationException("The session has expired.");
        }

        LastActivityAtUtc = now;
    }

    public void Rotate(string refreshTokenHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshTokenHash);
        Touch(now);
        RefreshTokenHash = refreshTokenHash;
        RotationVersion++;
    }

    public void Revoke(DateTimeOffset now)
    {
        RevokedAtUtc ??= now;
    }
}
