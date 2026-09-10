using System.Security.Cryptography;
using System.Text;
using FamilyJobsBoard.Application.Identity;
using Microsoft.AspNetCore.Identity;

namespace FamilyJobsBoard.Api.Features.Identity;

internal sealed class AspNetCorePinHasher : IPinHasher
{
    private static readonly IdentityMember DummyMember = new(
        Guid.Parse("e13a5824-9b63-43eb-a27c-0942db13f9b6"),
        "Unknown",
        "Member",
        null,
        "Unknown",
        Domain.Households.HouseholdRole.Adult,
        true,
        true);

    private readonly byte[] _pepper;
    private readonly PasswordHasher<IdentityMember> _hasher;
    private readonly string _dummyHash;

    public AspNetCorePinHasher(FamilyAuthenticationOptions options)
    {
        _pepper = options.PinPepper;
        _hasher = new PasswordHasher<IdentityMember>(Microsoft.Extensions.Options.Options.Create(
            new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
                IterationCount = options.PinHashIterations,
            }));
        _dummyHash = _hasher.HashPassword(DummyMember, Derive("000000"));
    }

    public string Hash(IdentityMember member, string pin) =>
        _hasher.HashPassword(member, Derive(pin));

    public bool Verify(IdentityMember member, string pin, string hash, out bool needsRehash)
    {
        var result = _hasher.VerifyHashedPassword(member, hash, Derive(pin));
        needsRehash = result == PasswordVerificationResult.SuccessRehashNeeded;
        return result != PasswordVerificationResult.Failed;
    }

    public void PerformDummyVerification(string pin)
    {
        _ = _hasher.VerifyHashedPassword(DummyMember, _dummyHash, Derive(pin));
    }

    private string Derive(string pin)
    {
        using var hmac = new HMACSHA256(_pepper);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(pin)));
    }
}
