using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Identity;

namespace FamilyJobsBoard.Application.Identity;

public sealed class IdentityService
{
    private readonly IIdentityRepository _repository;
    private readonly IPinHasher _pinHasher;
    private readonly IIdentityTokenService _tokens;
    private readonly IHouseholdClock _clock;

    public IdentityService(
        IIdentityRepository repository,
        IPinHasher pinHasher,
        IIdentityTokenService tokens,
        IHouseholdClock clock)
    {
        _repository = repository;
        _pinHasher = pinHasher;
        _tokens = tokens;
        _clock = clock;
    }

    public Task<IdentityStartState> GetStartAsync(CancellationToken cancellationToken) =>
        _repository.GetStartAsync(cancellationToken);

    public Task<IReadOnlyList<IdentityMember>> GetActiveMembersAsync(
        CancellationToken cancellationToken) =>
        _repository.GetActiveMembersAsync(cancellationToken);

    public Task<IdentityMember> CreateMemberAsync(
        CreateFamilyMember request,
        CancellationToken cancellationToken)
    {
        var firstName = RequiredName(request.FirstName);
        var surname = RequiredName(request.Surname);
        var nickname = OptionalName(request.Nickname);
        var role = request.Role?.Trim().ToLowerInvariant() switch
        {
            "adult" => HouseholdRole.Adult,
            "child" => HouseholdRole.Child,
            _ => throw new IdentityOperationException(IdentityError.InvalidMember),
        };

        return _repository.CreateMemberAsync(
            new HouseholdMember(Guid.NewGuid(), firstName, surname, role, nickname),
            cancellationToken);
    }

    public async Task<AuthenticatedIdentity> BootstrapAsync(
        BootstrapIdentity request,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeBootstrap(request);
        EnsurePin(normalized.Pin, HouseholdRole.Adult, IdentityError.InvalidBootstrap);
        var provisionalMember = new IdentityMember(
            normalized.MemberId ?? Guid.NewGuid(),
            normalized.FirstName ?? string.Empty,
            normalized.Surname,
            null,
            normalized.FirstName ?? string.Empty,
            HouseholdRole.Adult,
            true);
        var pinHash = _pinHasher.Hash(provisionalMember, normalized.Pin!);
        var stored = await _repository.BootstrapAsync(
            normalized,
            pinHash,
            _clock.UtcNow,
            cancellationToken);

        return stored.Status switch
        {
            BootstrapStoreStatus.Created => await CreateSessionAsync(stored.Member!, cancellationToken),
            BootstrapStoreStatus.AlreadyComplete => throw new IdentityOperationException(
                IdentityError.HouseholdAlreadyBootstrapped),
            BootstrapStoreStatus.AdultNotClaimable => throw new IdentityOperationException(
                IdentityError.AdultNotClaimable),
            _ => throw new IdentityOperationException(IdentityError.InvalidBootstrap),
        };
    }

    public async Task<AuthenticatedIdentity> SignInAsync(
        Guid memberId,
        string? pin,
        CancellationToken cancellationToken)
    {
        var candidate = memberId == Guid.Empty
            ? null
            : await _repository.GetCredentialAsync(memberId, cancellationToken);
        if (candidate is null)
        {
            _pinHasher.PerformDummyVerification(pin ?? string.Empty);
            throw new IdentityOperationException(IdentityError.InvalidCredentials);
        }

        if (candidate.LockedUntilUtc is not null && _clock.UtcNow < candidate.LockedUntilUtc)
        {
            throw new IdentityOperationException(IdentityError.TryAgainLater);
        }

        if (!IsValidPin(pin, candidate.Member.Role))
        {
            _pinHasher.PerformDummyVerification(pin ?? string.Empty);
            throw new IdentityOperationException(IdentityError.InvalidCredentials);
        }

        if (!_pinHasher.Verify(candidate.Member, pin!, candidate.PinHash, out var needsRehash))
        {
            var locked = await _repository.RecordFailedAttemptAsync(
                memberId,
                _clock.UtcNow,
                cancellationToken);
            throw new IdentityOperationException(
                locked ? IdentityError.TryAgainLater : IdentityError.InvalidCredentials);
        }

        await _repository.ResetFailedAttemptsAsync(
            memberId,
            needsRehash ? _pinHasher.Hash(candidate.Member, pin!) : null,
            _clock.UtcNow,
            cancellationToken);
        return await CreateSessionAsync(candidate.Member, cancellationToken);
    }

    public async Task<AuthenticatedIdentity> RefreshAsync(
        string? refreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)
            || !_tokens.TryReadOpaqueTokenId(refreshToken, out var sessionId))
        {
            throw new IdentityOperationException(IdentityError.SessionExpired);
        }

        var next = _tokens.CreateOpaqueToken(sessionId);
        var result = await _repository.RefreshSessionAsync(
            sessionId,
            _tokens.HashOpaqueToken(refreshToken),
            next.Hash,
            _clock.UtcNow,
            cancellationToken);
        if (result.Status != SessionRefreshStatus.Refreshed)
        {
            throw new IdentityOperationException(IdentityError.SessionExpired);
        }

        var access = _tokens.IssueAccessToken(result.Member!, result.SessionId, _clock.UtcNow);
        return new AuthenticatedIdentity(
            access.Value,
            access.ExpiresAtUtc,
            next.Value,
            result.Member!,
            result.SessionId);
    }

    public Task<bool> ValidateSessionAsync(
        Guid sessionId,
        Guid memberId,
        HouseholdRole role,
        CancellationToken cancellationToken) =>
        _repository.ValidateAndTouchSessionAsync(
            sessionId,
            memberId,
            role,
            _clock.UtcNow,
            cancellationToken);

    public Task LogoutAsync(Guid sessionId, CancellationToken cancellationToken) =>
        _repository.RevokeSessionAsync(sessionId, _clock.UtcNow, cancellationToken);

    public async Task<PinSetupGrant> IssuePinSetupAsync(
        Guid targetMemberId,
        Guid adultId,
        Guid adultSessionId,
        string? surname,
        CancellationToken cancellationToken)
    {
        var normalizedSurname = surname?.Trim();
        if (normalizedSurname?.Length > HouseholdMember.MaximumNameLength)
        {
            throw new IdentityOperationException(IdentityError.MemberNotEligible);
        }

        var generated = _tokens.CreateOpaqueToken(Guid.NewGuid());
        var result = await _repository.IssuePinSetupAsync(
            generated.Id,
            targetMemberId,
            adultId,
            adultSessionId,
            normalizedSurname,
            generated.Hash,
            _clock.UtcNow,
            cancellationToken);
        return result.Status switch
        {
            PinSetupIssueStatus.Issued => new PinSetupGrant(
                generated.Value,
                result.ExpiresAtUtc!.Value,
                result.Member!),
            PinSetupIssueStatus.MemberNotFound => throw new IdentityOperationException(
                IdentityError.MemberNotFound),
            PinSetupIssueStatus.AlreadySet => throw new IdentityOperationException(
                IdentityError.PinAlreadySet),
            _ => throw new IdentityOperationException(IdentityError.MemberNotEligible),
        };
    }

    public async Task<AuthenticatedIdentity> SetupPinAsync(
        string? setupToken,
        string? pin,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(setupToken)
            || !_tokens.TryReadOpaqueTokenId(setupToken, out var tokenId))
        {
            throw new IdentityOperationException(IdentityError.InvalidOrExpiredSetup);
        }

        var tokenHash = _tokens.HashOpaqueToken(setupToken);
        var target = await _repository.GetPinSetupTargetAsync(
            tokenId,
            tokenHash,
            _clock.UtcNow,
            cancellationToken);
        if (target is null)
        {
            throw new IdentityOperationException(IdentityError.InvalidOrExpiredSetup);
        }

        EnsurePin(pin, target.Role, IdentityError.InvalidOrExpiredSetup);
        var consumed = await _repository.ConsumePinSetupAsync(
            tokenId,
            tokenHash,
            _pinHasher.Hash(target, pin!),
            _clock.UtcNow,
            cancellationToken);
        if (consumed.Status != PinSetupConsumeStatus.Consumed)
        {
            throw new IdentityOperationException(IdentityError.InvalidOrExpiredSetup);
        }

        return await CreateSessionAsync(consumed.Member!, cancellationToken);
    }

    private async Task<AuthenticatedIdentity> CreateSessionAsync(
        IdentityMember member,
        CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        var refresh = _tokens.CreateOpaqueToken(sessionId);
        await _repository.CreateSessionAsync(
            sessionId,
            member.Id,
            member.Role,
            refresh.Hash,
            _clock.UtcNow,
            cancellationToken);
        var access = _tokens.IssueAccessToken(member, sessionId, _clock.UtcNow);
        return new AuthenticatedIdentity(
            access.Value,
            access.ExpiresAtUtc,
            refresh.Value,
            member,
            sessionId);
    }

    private static BootstrapIdentity NormalizeBootstrap(BootstrapIdentity request)
    {
        var mode = request.Mode?.Trim();
        var surname = request.Surname?.Trim();
        if (string.IsNullOrWhiteSpace(surname) || surname.Length > HouseholdMember.MaximumNameLength)
        {
            throw new IdentityOperationException(IdentityError.InvalidBootstrap);
        }

        if (mode == "createFirstAdult")
        {
            var firstName = request.FirstName?.Trim();
            if (request.MemberId is not null
                || string.IsNullOrWhiteSpace(firstName)
                || firstName.Length > HouseholdMember.MaximumNameLength)
            {
                throw new IdentityOperationException(IdentityError.InvalidBootstrap);
            }

            return request with { Mode = mode, FirstName = firstName, Surname = surname };
        }

        if (mode == "claimExistingAdult" && request.MemberId is not null)
        {
            return request with { Mode = mode, FirstName = null, Surname = surname };
        }

        throw new IdentityOperationException(IdentityError.InvalidBootstrap);
    }

    private static void EnsurePin(string? pin, HouseholdRole role, IdentityError error)
    {
        if (!IsValidPin(pin, role))
        {
            throw new IdentityOperationException(error);
        }
    }

    public static bool IsValidPin(string? pin, HouseholdRole role) => PinPolicy.IsValid(pin, role);

    private static string RequiredName(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > HouseholdMember.MaximumNameLength)
        {
            throw new IdentityOperationException(IdentityError.InvalidMember);
        }

        return normalized;
    }

    private static string? OptionalName(string? value)
    {
        var normalized = value?.Trim();
        if (normalized?.Length > HouseholdMember.MaximumNameLength)
        {
            throw new IdentityOperationException(IdentityError.InvalidMember);
        }

        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
