namespace FamilyJobsBoard.Domain.Identity;

public sealed class PinSetupToken
{
    private PinSetupToken()
    {
    }

    public PinSetupToken(
        Guid id,
        Guid targetMemberId,
        Guid authorizingAdultId,
        string tokenHash,
        DateTimeOffset now)
    {
        if (id == Guid.Empty || targetMemberId == Guid.Empty || authorizingAdultId == Guid.Empty)
        {
            throw new ArgumentException("A PIN setup token needs IDs.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        Id = id;
        TargetMemberId = targetMemberId;
        AuthorizingAdultId = authorizingAdultId;
        TokenHash = tokenHash;
        ExpiresAtUtc = now.AddMinutes(5);
    }

    public Guid Id { get; private set; }
    public Guid TargetMemberId { get; private set; }
    public Guid AuthorizingAdultId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool CanConsume(string tokenHash, DateTimeOffset now) =>
        ConsumedAtUtc is null
        && RevokedAtUtc is null
        && now < ExpiresAtUtc
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(TokenHash),
            System.Text.Encoding.UTF8.GetBytes(tokenHash));

    public void Consume(DateTimeOffset now) => ConsumedAtUtc = now;

    public void Revoke(DateTimeOffset now) => RevokedAtUtc ??= now;
}
