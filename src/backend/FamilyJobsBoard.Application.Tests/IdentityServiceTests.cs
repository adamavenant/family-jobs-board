using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Application.Identity;
using FamilyJobsBoard.Domain.Households;
using Xunit;

namespace FamilyJobsBoard.Application.Tests;

public sealed class IdentityServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Unknown_member_performs_dummy_verification()
    {
        var repository = new RecordingRepository();
        var hasher = new RecordingHasher();
        var service = NewService(repository, hasher);

        var error = await Assert.ThrowsAsync<IdentityOperationException>(
            () => service.SignInAsync(Guid.NewGuid(), "123456", CancellationToken.None));

        Assert.Equal(IdentityError.InvalidCredentials, error.Error);
        Assert.Equal(1, hasher.DummyVerificationCount);
        Assert.Equal(0, repository.FailedAttemptCount);
    }

    [Fact]
    public async Task Malformed_pin_uses_dummy_work_without_locking_the_profile()
    {
        var member = Adult();
        var repository = new RecordingRepository
        {
            Candidate = new CredentialCandidate(member, "old-hash", null),
        };
        var hasher = new RecordingHasher();
        var service = NewService(repository, hasher);

        var error = await Assert.ThrowsAsync<IdentityOperationException>(
            () => service.SignInAsync(member.Id, "123", CancellationToken.None));

        Assert.Equal(IdentityError.InvalidCredentials, error.Error);
        Assert.Equal(1, hasher.DummyVerificationCount);
        Assert.Equal(0, repository.FailedAttemptCount);
    }

    [Fact]
    public async Task Successful_sign_in_rehashes_and_creates_a_session()
    {
        var member = Adult();
        var repository = new RecordingRepository
        {
            Candidate = new CredentialCandidate(member, "old-hash", null),
        };
        var hasher = new RecordingHasher { VerificationSucceeds = true, NeedsRehash = true };
        var service = NewService(repository, hasher);

        var result = await service.SignInAsync(member.Id, "012345", CancellationToken.None);

        Assert.Equal(member.Id, result.Member.Id);
        Assert.Equal("new-hash", repository.ReplacementHash);
        Assert.Equal(1, repository.CreatedSessionCount);
    }

    private static IdentityService NewService(RecordingRepository repository, RecordingHasher hasher) =>
        new(repository, hasher, new StubTokens(), new FixedClock());

    private static IdentityMember Adult() =>
        new(Guid.NewGuid(), "Addie", "Avenant", null, "Addie", HouseholdRole.Adult, true);

    private sealed class RecordingHasher : IPinHasher
    {
        public bool VerificationSucceeds { get; init; }
        public bool NeedsRehash { get; init; }
        public int DummyVerificationCount { get; private set; }

        public string Hash(IdentityMember member, string pin) => "new-hash";

        public bool Verify(IdentityMember member, string pin, string hash, out bool needsRehash)
        {
            needsRehash = NeedsRehash;
            return VerificationSucceeds;
        }

        public void PerformDummyVerification(string pin) => DummyVerificationCount++;
    }

    private sealed class StubTokens : IIdentityTokenService
    {
        public GeneratedOpaqueToken CreateOpaqueToken(Guid id) => new(id, $"{id:N}.opaque", "refresh-hash");
        public bool TryReadOpaqueTokenId(string value, out Guid id) => Guid.TryParse(value, out id);
        public string HashOpaqueToken(string value) => "refresh-hash";
        public IssuedAccessToken IssueAccessToken(IdentityMember member, Guid sessionId, DateTimeOffset now) =>
            new("access-token", now.AddMinutes(5));
    }

    private sealed class FixedClock : IHouseholdClock
    {
        public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class RecordingRepository : IIdentityRepository
    {
        public CredentialCandidate? Candidate { get; init; }
        public int FailedAttemptCount { get; private set; }
        public int CreatedSessionCount { get; private set; }
        public string? ReplacementHash { get; private set; }

        public Task<CredentialCandidate?> GetCredentialAsync(Guid memberId, CancellationToken cancellationToken) =>
            Task.FromResult(Candidate);

        public Task<bool> RecordFailedAttemptAsync(Guid memberId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            FailedAttemptCount++;
            return Task.FromResult(false);
        }

        public Task ResetFailedAttemptsAsync(
            Guid memberId,
            string? replacementHash,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            ReplacementHash = replacementHash;
            return Task.CompletedTask;
        }

        public Task CreateSessionAsync(
            Guid sessionId,
            Guid memberId,
            HouseholdRole role,
            string refreshTokenHash,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            CreatedSessionCount++;
            return Task.CompletedTask;
        }

        public Task<IdentityStartState> GetStartAsync(CancellationToken cancellationToken) => throw Unused();
        public Task<BootstrapStoreResult> BootstrapAsync(BootstrapIdentity request, string pinHash, DateTimeOffset now, CancellationToken cancellationToken) => throw Unused();
        public Task<SessionRefreshResult> RefreshSessionAsync(Guid sessionId, string suppliedRefreshHash, string nextRefreshHash, DateTimeOffset now, CancellationToken cancellationToken) => throw Unused();
        public Task<bool> ValidateAndTouchSessionAsync(Guid sessionId, Guid memberId, HouseholdRole role, DateTimeOffset now, CancellationToken cancellationToken) => throw Unused();
        public Task RevokeSessionAsync(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken) => throw Unused();
        public Task<PinSetupIssueResult> IssuePinSetupAsync(Guid tokenId, Guid targetMemberId, Guid adultId, Guid adultSessionId, string? surname, string tokenHash, DateTimeOffset now, CancellationToken cancellationToken) => throw Unused();
        public Task<IdentityMember?> GetPinSetupTargetAsync(Guid tokenId, string tokenHash, DateTimeOffset now, CancellationToken cancellationToken) => throw Unused();
        public Task<PinSetupConsumeResult> ConsumePinSetupAsync(Guid tokenId, string tokenHash, string pinHash, DateTimeOffset now, CancellationToken cancellationToken) => throw Unused();

        private static NotSupportedException Unused() => new("This operation is not used by the test.");
    }
}
