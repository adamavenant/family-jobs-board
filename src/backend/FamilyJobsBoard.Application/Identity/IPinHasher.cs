using FamilyJobsBoard.Domain.Households;

namespace FamilyJobsBoard.Application.Identity;

public interface IPinHasher
{
    string Hash(IdentityMember member, string pin);

    bool Verify(IdentityMember member, string pin, string hash, out bool needsRehash);

    void PerformDummyVerification(string pin);
}

public interface IIdentityTokenService
{
    GeneratedOpaqueToken CreateOpaqueToken(Guid id);

    bool TryReadOpaqueTokenId(string value, out Guid id);

    string HashOpaqueToken(string value);

    IssuedAccessToken IssueAccessToken(IdentityMember member, Guid sessionId, DateTimeOffset now);
}
