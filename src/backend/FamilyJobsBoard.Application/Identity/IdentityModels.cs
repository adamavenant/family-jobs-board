using FamilyJobsBoard.Domain.Households;

namespace FamilyJobsBoard.Application.Identity;

public enum IdentityStartMode
{
    CreateFirstAdult,
    ClaimExistingAdult,
    SignIn,
}

public sealed record IdentityMember(
    Guid Id,
    string FirstName,
    string? Surname,
    string? Nickname,
    string DisplayName,
    HouseholdRole Role,
    bool IsCredentialReady);

public sealed record IdentityStartState(
    IdentityStartMode Mode,
    IReadOnlyList<IdentityMember> Members);

public sealed record BootstrapIdentity(
    string Mode,
    Guid? MemberId,
    string? FirstName,
    string? Surname,
    string? Pin);

public sealed record AuthenticatedIdentity(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    IdentityMember Member,
    Guid SessionId);

public sealed record PinSetupGrant(
    string SetupToken,
    DateTimeOffset ExpiresAtUtc,
    IdentityMember Member);

public sealed record CredentialCandidate(IdentityMember Member, string PinHash, DateTimeOffset? LockedUntilUtc);

public enum BootstrapStoreStatus
{
    Created,
    AlreadyComplete,
    InvalidMode,
    AdultNotClaimable,
}

public sealed record BootstrapStoreResult(BootstrapStoreStatus Status, IdentityMember? Member);

public enum SessionRefreshStatus
{
    Refreshed,
    NotFound,
    Expired,
    Replay,
}

public sealed record SessionRefreshResult(
    SessionRefreshStatus Status,
    IdentityMember? Member,
    Guid SessionId);

public enum PinSetupIssueStatus
{
    Issued,
    MemberNotFound,
    AlreadySet,
    MemberNotEligible,
}

public sealed record PinSetupIssueResult(
    PinSetupIssueStatus Status,
    IdentityMember? Member,
    DateTimeOffset? ExpiresAtUtc);

public enum PinSetupConsumeStatus
{
    Consumed,
    InvalidOrExpired,
}

public sealed record PinSetupConsumeResult(PinSetupConsumeStatus Status, IdentityMember? Member);

public sealed record GeneratedOpaqueToken(Guid Id, string Value, string Hash);

public sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAtUtc);

public enum IdentityError
{
    InvalidBootstrap,
    HouseholdAlreadyBootstrapped,
    AdultNotClaimable,
    InvalidCredentials,
    TryAgainLater,
    SessionExpired,
    MemberNotFound,
    PinAlreadySet,
    MemberNotEligible,
    InvalidOrExpiredSetup,
}

public sealed class IdentityOperationException : Exception
{
    public IdentityOperationException(IdentityError error)
        : base(error.ToString())
    {
        Error = error;
    }

    public IdentityError Error { get; }
}
