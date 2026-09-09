using System.Data;
using FamilyJobsBoard.Application.Identity;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Identity;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyJobsBoard.Infrastructure.Identity;

public sealed class EfIdentityRepository : IIdentityRepository
{
    private readonly AppDbContext _database;

    public EfIdentityRepository(AppDbContext database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<IdentityMember>> GetMembersAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = _database.HouseholdMembers.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(member => member.IsActive);
        }

        var members = await query.ToListAsync(cancellationToken);
        var readyIds = await _database.MemberCredentials
            .AsNoTracking()
            .Where(credential => credential.State == CredentialState.Ready)
            .Select(credential => credential.MemberId)
            .ToListAsync(cancellationToken);
        return MapMembers(members, readyIds.ToHashSet());
    }

    public async Task<IdentityMember> CreateMemberAsync(
        HouseholdMember member,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _database.Database.BeginTransactionAsync(cancellationToken);
        _database.HouseholdMembers.Add(member);
        _database.MemberCredentials.Add(new MemberCredential(member.Id));
        await _database.SaveChangesAsync(cancellationToken);

        var members = await GetMembersAsync(false, cancellationToken);
        var created = members.Single(candidate => candidate.Id == member.Id);
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<IdentityMember?> UpdateMemberAsync(
        Guid memberId,
        string firstName,
        string surname,
        string? nickname,
        Guid actorMemberId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var member = await _database.HouseholdMembers.SingleOrDefaultAsync(
            candidate => candidate.Id == memberId,
            cancellationToken);
        if (member is null)
        {
            return null;
        }

        member.UpdateProfile(firstName, surname, nickname, actorMemberId, now);
        await _database.SaveChangesAsync(cancellationToken);
        return (await GetMembersAsync(true, cancellationToken))
            .Single(candidate => candidate.Id == memberId);
    }

    public async Task<IdentityMember?> SetMemberActiveAsync(
        Guid memberId,
        bool isActive,
        Guid actorMemberId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _database.Database.BeginTransactionAsync(cancellationToken);
        var member = await _database.HouseholdMembers
            .FromSqlInterpolated($"SELECT * FROM household_members WHERE id = {memberId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (member is null)
        {
            return null;
        }

        if (isActive)
        {
            member.Restore(actorMemberId, now);
        }
        else if (member.IsActive)
        {
            member.Deactivate(actorMemberId, now);
            var sessions = await _database.AuthSessions
                .Where(session => session.MemberId == memberId && session.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var session in sessions)
            {
                session.Revoke(now);
            }

            var setupTokens = await _database.PinSetupTokens
                .Where(token => token.TargetMemberId == memberId
                    && token.ConsumedAtUtc == null
                    && token.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var token in setupTokens)
            {
                token.Revoke(now);
            }
        }

        await _database.SaveChangesAsync(cancellationToken);
        var mapped = (await GetMembersAsync(true, cancellationToken))
            .Single(candidate => candidate.Id == memberId);
        await transaction.CommitAsync(cancellationToken);
        return mapped;
    }

    public async Task<IdentityStartState> GetStartAsync(CancellationToken cancellationToken)
    {
        var bootstrap = await _database.HouseholdBootstraps
            .AsNoTracking()
            .SingleAsync(bootstrap => bootstrap.Id == HouseholdBootstrap.SingletonId, cancellationToken);
        var members = await _database.HouseholdMembers.AsNoTracking().ToListAsync(cancellationToken);
        var readyIds = await _database.MemberCredentials
            .AsNoTracking()
            .Where(credential => credential.State == CredentialState.Ready)
            .Select(credential => credential.MemberId)
            .ToListAsync(cancellationToken);
        var ready = readyIds.ToHashSet();

        if (bootstrap.State == BootstrapState.Required)
        {
            if (members.Count == 0)
            {
                return new IdentityStartState(IdentityStartMode.CreateFirstAdult, []);
            }

            return new IdentityStartState(
                IdentityStartMode.ClaimExistingAdult,
                MapMembers(members.Where(member => member.IsActive && member.IsAdult), ready));
        }

        return new IdentityStartState(
            IdentityStartMode.SignIn,
            MapMembers(members.Where(member => member.IsActive && ready.Contains(member.Id)), ready));
    }

    public async Task<BootstrapStoreResult> BootstrapAsync(
        BootstrapIdentity request,
        string pinHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _database.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var bootstrap = await _database.HouseholdBootstraps
            .FromSqlRaw("SELECT * FROM household_bootstrap WHERE id = 1 FOR UPDATE")
            .SingleAsync(cancellationToken);
        if (bootstrap.State == BootstrapState.Complete)
        {
            return new BootstrapStoreResult(BootstrapStoreStatus.AlreadyComplete, null);
        }

        var anyMember = await _database.HouseholdMembers.AnyAsync(cancellationToken);
        var anyReady = await _database.MemberCredentials.AnyAsync(
            credential => credential.State == CredentialState.Ready,
            cancellationToken);
        HouseholdMember member;
        MemberCredential credential;

        if (request.Mode == "createFirstAdult" && !anyMember)
        {
            member = new HouseholdMember(
                Guid.NewGuid(),
                request.FirstName!,
                request.Surname,
                HouseholdRole.Adult);
            credential = new MemberCredential(member.Id);
            _database.HouseholdMembers.Add(member);
            _database.MemberCredentials.Add(credential);
        }
        else if (request.Mode == "claimExistingAdult" && anyMember && !anyReady)
        {
            var claimable = await _database.HouseholdMembers.SingleOrDefaultAsync(
                candidate => candidate.Id == request.MemberId && candidate.Role == HouseholdRole.Adult,
                cancellationToken);
            if (claimable is null)
            {
                return new BootstrapStoreResult(BootstrapStoreStatus.AdultNotClaimable, null);
            }

            member = claimable;
            credential = await _database.MemberCredentials.SingleOrDefaultAsync(
                    candidate => candidate.MemberId == member.Id,
                    cancellationToken)
                ?? new MemberCredential(member.Id);
            if (_database.Entry(credential).State == EntityState.Detached)
            {
                _database.MemberCredentials.Add(credential);
            }

            member.SetSurname(request.Surname!);
        }
        else
        {
            return new BootstrapStoreResult(
                request.Mode is "createFirstAdult" or "claimExistingAdult"
                    ? BootstrapStoreStatus.AdultNotClaimable
                    : BootstrapStoreStatus.InvalidMode,
                null);
        }

        credential.SetPin(pinHash, now);
        bootstrap.Complete(member.Id, now);
        await _database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BootstrapStoreResult(
            BootstrapStoreStatus.Created,
            MapMember(member, true, member.DisplayName));
    }

    public async Task<CredentialCandidate?> GetCredentialAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var member = await _database.HouseholdMembers.AsNoTracking().SingleOrDefaultAsync(
            member => member.Id == memberId && member.IsActive,
            cancellationToken);
        var credential = await _database.MemberCredentials.AsNoTracking().SingleOrDefaultAsync(
            credential => credential.MemberId == memberId && credential.State == CredentialState.Ready,
            cancellationToken);
        return member is null || credential?.PinHash is null || member.Surname is null
            ? null
            : new CredentialCandidate(
                MapMember(member, true, member.DisplayName),
                credential.PinHash,
                credential.LockedUntilUtc);
    }

    public async Task<bool> RecordFailedAttemptAsync(
        Guid memberId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _database.Database.BeginTransactionAsync(cancellationToken);
        var credential = await LockedCredentialAsync(memberId, cancellationToken);
        if (credential is null)
        {
            return false;
        }

        credential.RecordFailure(now);
        await _database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return credential.LockedUntilUtc is not null && now < credential.LockedUntilUtc;
    }

    public async Task ResetFailedAttemptsAsync(
        Guid memberId,
        string? replacementHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _database.Database.BeginTransactionAsync(cancellationToken);
        var credential = await LockedCredentialAsync(memberId, cancellationToken)
            ?? throw new InvalidOperationException("Credential disappeared during sign-in.");
        if (replacementHash is null)
        {
            credential.ResetFailures();
        }
        else
        {
            credential.SetPin(replacementHash, now);
        }

        await _database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CreateSessionAsync(
        Guid sessionId,
        Guid memberId,
        HouseholdRole role,
        string refreshTokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        _database.AuthSessions.Add(new AuthSession(sessionId, memberId, role, refreshTokenHash, now));
        await _database.SaveChangesAsync(cancellationToken);
    }

    public async Task<SessionRefreshResult> RefreshSessionAsync(
        Guid sessionId,
        string suppliedRefreshHash,
        string nextRefreshHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _database.Database.BeginTransactionAsync(cancellationToken);
        var session = await _database.AuthSessions
            .FromSqlInterpolated($"SELECT * FROM auth_sessions WHERE id = {sessionId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (session is null)
        {
            return new SessionRefreshResult(SessionRefreshStatus.NotFound, null, sessionId);
        }

        if (!session.IsActive(now))
        {
            session.Revoke(now);
            await _database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new SessionRefreshResult(SessionRefreshStatus.Expired, null, sessionId);
        }

        if (!CryptographicEquals(session.RefreshTokenHash, suppliedRefreshHash))
        {
            session.Revoke(now);
            await _database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new SessionRefreshResult(SessionRefreshStatus.Replay, null, sessionId);
        }

        var member = await _database.HouseholdMembers.AsNoTracking().SingleAsync(
            member => member.Id == session.MemberId,
            cancellationToken);
        if (!member.IsActive)
        {
            session.Revoke(now);
            await _database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new SessionRefreshResult(SessionRefreshStatus.Expired, null, sessionId);
        }

        session.Rotate(nextRefreshHash, now);
        await _database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new SessionRefreshResult(
            SessionRefreshStatus.Refreshed,
            MapMember(member, true, member.DisplayName),
            session.Id);
    }

    public async Task<bool> ValidateAndTouchSessionAsync(
        Guid sessionId,
        Guid memberId,
        HouseholdRole role,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var session = await _database.AuthSessions.SingleOrDefaultAsync(
            session => session.Id == sessionId,
            cancellationToken);
        var memberIsActive = session is not null
            && await _database.HouseholdMembers.AnyAsync(
                member => member.Id == memberId && member.IsActive,
                cancellationToken);
        if (session is null
            || session.MemberId != memberId
            || session.Role != role
            || !memberIsActive
            || !session.IsActive(now))
        {
            if (session is { RevokedAtUtc: null })
            {
                session.Revoke(now);
                await _database.SaveChangesAsync(cancellationToken);
            }

            return false;
        }

        if (now - session.LastActivityAtUtc >= TimeSpan.FromMinutes(1))
        {
            session.Touch(now);
            await _database.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task RevokeSessionAsync(
        Guid sessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var session = await _database.AuthSessions.SingleOrDefaultAsync(
            session => session.Id == sessionId,
            cancellationToken);
        if (session is null)
        {
            return;
        }

        session.Revoke(now);
        await _database.SaveChangesAsync(cancellationToken);
    }

    public async Task<PinSetupIssueResult> IssuePinSetupAsync(
        Guid tokenId,
        Guid targetMemberId,
        Guid adultId,
        Guid adultSessionId,
        string? surname,
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _database.Database.BeginTransactionAsync(cancellationToken);
        var adultSession = await _database.AuthSessions
            .FromSqlInterpolated($"SELECT * FROM auth_sessions WHERE id = {adultSessionId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (adultSession is null
            || adultSession.MemberId != adultId
            || adultSession.Role != HouseholdRole.Adult
            || !adultSession.IsActive(now))
        {
            return new PinSetupIssueResult(PinSetupIssueStatus.MemberNotEligible, null, null);
        }

        var target = await _database.HouseholdMembers.SingleOrDefaultAsync(
            member => member.Id == targetMemberId,
            cancellationToken);
        if (target is null)
        {
            return new PinSetupIssueResult(PinSetupIssueStatus.MemberNotFound, null, null);
        }

        if (!target.IsActive)
        {
            return new PinSetupIssueResult(PinSetupIssueStatus.MemberNotEligible, null, null);
        }

        var credential = await LockedCredentialAsync(targetMemberId, cancellationToken);
        if (credential?.State == CredentialState.Ready)
        {
            return new PinSetupIssueResult(PinSetupIssueStatus.AlreadySet, null, null);
        }

        if (credential is null)
        {
            return new PinSetupIssueResult(PinSetupIssueStatus.MemberNotEligible, null, null);
        }

        if (target.Surname is null)
        {
            if (string.IsNullOrWhiteSpace(surname))
            {
                return new PinSetupIssueResult(PinSetupIssueStatus.MemberNotEligible, null, null);
            }

            target.SetSurname(surname);
        }

        var previous = await _database.PinSetupTokens
            .Where(token => token.TargetMemberId == targetMemberId
                && token.ConsumedAtUtc == null
                && token.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var token in previous)
        {
            token.Revoke(now);
        }

        var setup = new PinSetupToken(tokenId, targetMemberId, adultId, tokenHash, now);
        _database.PinSetupTokens.Add(setup);
        adultSession.Revoke(now);
        await _database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PinSetupIssueResult(
            PinSetupIssueStatus.Issued,
            MapMember(target, false, target.DisplayName),
            setup.ExpiresAtUtc);
    }

    public async Task<IdentityMember?> GetPinSetupTargetAsync(
        Guid tokenId,
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var token = await _database.PinSetupTokens.AsNoTracking().SingleOrDefaultAsync(
            token => token.Id == tokenId,
            cancellationToken);
        if (token is null || !token.CanConsume(tokenHash, now))
        {
            return null;
        }

        var target = await _database.HouseholdMembers.AsNoTracking().SingleAsync(
            member => member.Id == token.TargetMemberId,
            cancellationToken);
        var credential = await _database.MemberCredentials.AsNoTracking().SingleAsync(
            credential => credential.MemberId == target.Id,
            cancellationToken);
        return credential.State == CredentialState.NotSet && target.Surname is not null
            ? MapMember(target, false, target.DisplayName)
            : null;
    }

    public async Task<PinSetupConsumeResult> ConsumePinSetupAsync(
        Guid tokenId,
        string tokenHash,
        string pinHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _database.Database.BeginTransactionAsync(cancellationToken);
        var token = await _database.PinSetupTokens
            .FromSqlInterpolated($"SELECT * FROM pin_setup_tokens WHERE id = {tokenId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (token is null || !token.CanConsume(tokenHash, now))
        {
            return new PinSetupConsumeResult(PinSetupConsumeStatus.InvalidOrExpired, null);
        }

        var credential = await LockedCredentialAsync(token.TargetMemberId, cancellationToken);
        var member = await _database.HouseholdMembers.SingleAsync(
            member => member.Id == token.TargetMemberId,
            cancellationToken);
        if (credential is null || credential.State != CredentialState.NotSet || member.Surname is null)
        {
            return new PinSetupConsumeResult(PinSetupConsumeStatus.InvalidOrExpired, null);
        }

        credential.SetPin(pinHash, now);
        token.Consume(now);
        await _database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PinSetupConsumeResult(
            PinSetupConsumeStatus.Consumed,
            MapMember(member, true, member.DisplayName));
    }

    private Task<MemberCredential?> LockedCredentialAsync(Guid memberId, CancellationToken cancellationToken) =>
        _database.MemberCredentials
            .FromSqlInterpolated($"SELECT * FROM member_credentials WHERE member_id = {memberId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private static IReadOnlyList<IdentityMember> MapMembers(
        IEnumerable<HouseholdMember> source,
        IReadOnlySet<Guid> readyIds)
    {
        var members = source.OrderBy(member => member.DisplayName).ToArray();
        var duplicateNames = members
            .GroupBy(member => member.DisplayName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return members
            .Select(member => MapMember(
                member,
                readyIds.Contains(member.Id),
                duplicateNames.Contains(member.DisplayName) && member.Surname is not null
                    ? $"{member.DisplayName} {member.Surname}"
                    : member.DisplayName))
            .ToArray();
    }

    private static IdentityMember MapMember(HouseholdMember member, bool ready, string displayName) =>
        new(
            member.Id,
            member.FirstName,
            member.Surname,
            member.Nickname,
            displayName,
            member.Role,
            ready,
            member.IsActive);

    private static bool CryptographicEquals(string left, string right) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(left),
            System.Text.Encoding.UTF8.GetBytes(right));
}
