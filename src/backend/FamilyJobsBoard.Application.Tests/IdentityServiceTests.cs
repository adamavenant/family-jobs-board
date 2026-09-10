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

    [Fact]
    public async Task Creating_a_member_normalizes_names_and_starts_without_a_PIN()
    {
        var repository = new RecordingRepository();
        var service = NewService(repository, new RecordingHasher());

        var created = await service.CreateMemberAsync(
            new CreateFamilyMember("  Fred  ", "  Avenant ", "   ", "child"),
            CancellationToken.None);

        Assert.Equal("Fred", created.FirstName);
        Assert.Equal("Avenant", created.Surname);
        Assert.Null(created.Nickname);
        Assert.Equal(HouseholdRole.Child, created.Role);
        Assert.False(created.IsCredentialReady);
        Assert.NotNull(repository.CreatedMember);
    }

    [Theory]
    [InlineData("", "Avenant", "child")]
    [InlineData("Fred", "", "child")]
    [InlineData("Fred", "Avenant", "visitor")]
    public async Task Invalid_member_details_are_rejected_before_persistence(
        string firstName,
        string surname,
        string role)
    {
        var repository = new RecordingRepository();
        var service = NewService(repository, new RecordingHasher());

        var error = await Assert.ThrowsAsync<IdentityOperationException>(() =>
            service.CreateMemberAsync(
                new CreateFamilyMember(firstName, surname, null, role),
                CancellationToken.None));

        Assert.Equal(IdentityError.InvalidMember, error.Error);
        Assert.Null(repository.CreatedMember);
    }

    [Fact]
    public async Task Over_length_member_names_are_rejected_before_persistence()
    {
        var repository = new RecordingRepository();
        var service = NewService(repository, new RecordingHasher());

        var error = await Assert.ThrowsAsync<IdentityOperationException>(() =>
            service.CreateMemberAsync(
                new CreateFamilyMember(
                    new string('x', HouseholdMember.MaximumNameLength + 1),
                    "Avenant",
                    null,
                    "child"),
                CancellationToken.None));

        Assert.Equal(IdentityError.InvalidMember, error.Error);
        Assert.Null(repository.CreatedMember);
    }

    [Fact]
    public async Task Updating_a_member_normalizes_names_and_records_the_actor()
    {
        var member = Adult();
        var actorId = Guid.NewGuid();
        var repository = new RecordingRepository { MemberResult = member };
        var service = NewService(repository, new RecordingHasher());

        var updated = await service.UpdateMemberAsync(
            member.Id,
            new UpdateFamilyMember(" Addie ", " Avenant ", " Ads "),
            actorId,
            CancellationToken.None);

        Assert.Equal(member.Id, updated.Id);
        Assert.Equal(("Addie", "Avenant", "Ads"), repository.UpdatedNames);
        Assert.Equal(actorId, repository.ActorMemberId);
        Assert.Equal(Now, repository.MutationTime);
    }

    [Fact]
    public async Task Invalid_member_update_is_rejected_before_persistence()
    {
        var repository = new RecordingRepository { MemberResult = Adult() };
        var service = NewService(repository, new RecordingHasher());

        var error = await Assert.ThrowsAsync<IdentityOperationException>(() =>
            service.UpdateMemberAsync(
                Guid.NewGuid(),
                new UpdateFamilyMember("", "Avenant", null),
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal(IdentityError.InvalidMember, error.Error);
        Assert.Null(repository.UpdatedNames);
    }

    [Fact]
    public async Task An_adult_cannot_deactivate_their_own_profile()
    {
        var memberId = Guid.NewGuid();
        var repository = new RecordingRepository { MemberResult = Adult() };
        var service = NewService(repository, new RecordingHasher());

        var error = await Assert.ThrowsAsync<IdentityOperationException>(() =>
            service.DeactivateMemberAsync(memberId, memberId, CancellationToken.None));

        Assert.Equal(IdentityError.CannotDeactivateSelf, error.Error);
        Assert.Null(repository.RequestedActiveState);
    }

    [Fact]
    public async Task An_adult_cannot_reset_their_own_PIN()
    {
        var memberId = Guid.NewGuid();
        var repository = new RecordingRepository();
        var service = NewService(repository, new RecordingHasher());

        var error = await Assert.ThrowsAsync<IdentityOperationException>(() =>
            service.IssuePinResetAsync(memberId, memberId, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(IdentityError.CannotResetSelf, error.Error);
        Assert.Null(repository.ResetTargetMemberId);
    }

    [Fact]
    public async Task PIN_reset_issues_a_private_handoff_for_the_target()
    {
        var target = Adult() with { Id = Guid.NewGuid() };
        var actorId = Guid.NewGuid();
        var repository = new RecordingRepository
        {
            ResetResult = new PinResetIssueResult(
                PinResetIssueStatus.Issued,
                target with { IsCredentialReady = false },
                Now.AddMinutes(5)),
        };
        var service = NewService(repository, new RecordingHasher());

        var grant = await service.IssuePinResetAsync(
            target.Id,
            actorId,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(target.Id, grant.Member.Id);
        Assert.Equal(Now.AddMinutes(5), grant.ExpiresAtUtc);
        Assert.Equal(target.Id, repository.ResetTargetMemberId);
        Assert.Equal(actorId, repository.ActorMemberId);
    }

    private static IdentityService NewService(RecordingRepository repository, RecordingHasher hasher) =>
        new(repository, hasher, new StubTokens(), new FixedClock());

    private static IdentityMember Adult() =>
        new(Guid.NewGuid(), "Addie", "Avenant", null, "Addie", HouseholdRole.Adult, true, true);

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
        public HouseholdMember? CreatedMember { get; private set; }
        public IdentityMember? MemberResult { get; init; }
        public (string FirstName, string Surname, string? Nickname)? UpdatedNames { get; private set; }
        public bool? RequestedActiveState { get; private set; }
        public Guid? ActorMemberId { get; private set; }
        public DateTimeOffset? MutationTime { get; private set; }
        public PinResetIssueResult ResetResult { get; init; } =
            new(PinResetIssueStatus.MemberNotEligible, null, null);
        public Guid? ResetTargetMemberId { get; private set; }

        public Task<IReadOnlyList<IdentityMember>> GetMembersAsync(
            bool includeInactive,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IdentityMember>>([]);

        public Task<IdentityMember> CreateMemberAsync(
            HouseholdMember member,
            CancellationToken cancellationToken)
        {
            CreatedMember = member;
            return Task.FromResult(new IdentityMember(
                member.Id,
                member.FirstName,
                member.Surname,
                member.Nickname,
                member.DisplayName,
                member.Role,
                false,
                true));
        }

        public Task<IdentityMember?> UpdateMemberAsync(
            Guid memberId,
            string firstName,
            string surname,
            string? nickname,
            Guid actorMemberId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            UpdatedNames = (firstName, surname, nickname);
            ActorMemberId = actorMemberId;
            MutationTime = now;
            return Task.FromResult(MemberResult);
        }

        public Task<IdentityMember?> SetMemberActiveAsync(
            Guid memberId,
            bool isActive,
            Guid actorMemberId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            RequestedActiveState = isActive;
            ActorMemberId = actorMemberId;
            MutationTime = now;
            return Task.FromResult(MemberResult);
        }

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
        public Task<PinResetIssueResult> IssuePinResetAsync(Guid tokenId, Guid targetMemberId, Guid adultId, Guid adultSessionId, string tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
        {
            ResetTargetMemberId = targetMemberId;
            ActorMemberId = adultId;
            MutationTime = now;
            return Task.FromResult(ResetResult);
        }
        public Task<IdentityMember?> GetPinSetupTargetAsync(Guid tokenId, string tokenHash, DateTimeOffset now, CancellationToken cancellationToken) => throw Unused();
        public Task<PinSetupConsumeResult> ConsumePinSetupAsync(Guid tokenId, string tokenHash, string pinHash, DateTimeOffset now, CancellationToken cancellationToken) => throw Unused();

        private static NotSupportedException Unused() => new("This operation is not used by the test.");
    }
}
