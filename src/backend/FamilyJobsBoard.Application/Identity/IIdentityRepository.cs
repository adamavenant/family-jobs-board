using FamilyJobsBoard.Domain.Households;

namespace FamilyJobsBoard.Application.Identity;

public interface IIdentityRepository
{
    Task<IReadOnlyList<IdentityMember>> GetActiveMembersAsync(
        CancellationToken cancellationToken);

    Task<IdentityMember> CreateMemberAsync(
        HouseholdMember member,
        CancellationToken cancellationToken);

    Task<IdentityStartState> GetStartAsync(CancellationToken cancellationToken);

    Task<BootstrapStoreResult> BootstrapAsync(
        BootstrapIdentity request,
        string pinHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<CredentialCandidate?> GetCredentialAsync(Guid memberId, CancellationToken cancellationToken);

    Task<bool> RecordFailedAttemptAsync(Guid memberId, DateTimeOffset now, CancellationToken cancellationToken);

    Task ResetFailedAttemptsAsync(
        Guid memberId,
        string? replacementHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task CreateSessionAsync(
        Guid sessionId,
        Guid memberId,
        HouseholdRole role,
        string refreshTokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<SessionRefreshResult> RefreshSessionAsync(
        Guid sessionId,
        string suppliedRefreshHash,
        string nextRefreshHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<bool> ValidateAndTouchSessionAsync(
        Guid sessionId,
        Guid memberId,
        HouseholdRole role,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task RevokeSessionAsync(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<PinSetupIssueResult> IssuePinSetupAsync(
        Guid tokenId,
        Guid targetMemberId,
        Guid adultId,
        Guid adultSessionId,
        string? surname,
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<IdentityMember?> GetPinSetupTargetAsync(
        Guid tokenId,
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<PinSetupConsumeResult> ConsumePinSetupAsync(
        Guid tokenId,
        string tokenHash,
        string pinHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
