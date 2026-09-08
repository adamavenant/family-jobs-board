using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Identity;
using Xunit;

namespace FamilyJobsBoard.Domain.Tests;

public sealed class IdentityTests
{
    [Fact]
    public void Household_member_normalizes_administration_names()
    {
        var member = new HouseholdMember(
            Guid.NewGuid(),
            "  Fred  ",
            "  Avenant  ",
            HouseholdRole.Child,
            "  Fredster  ");

        Assert.Equal("Fred", member.FirstName);
        Assert.Equal("Avenant", member.Surname);
        Assert.Equal("Fredster", member.Nickname);
        Assert.Equal(HouseholdRole.Child, member.Role);
        Assert.True(member.IsActive);
    }

    [Theory]
    [InlineData("0123", HouseholdRole.Child, true)]
    [InlineData("123", HouseholdRole.Child, false)]
    [InlineData("01234", HouseholdRole.Child, false)]
    [InlineData("012345", HouseholdRole.Adult, true)]
    [InlineData("12345", HouseholdRole.Adult, false)]
    [InlineData("0123456", HouseholdRole.Adult, false)]
    [InlineData("12 345", HouseholdRole.Adult, false)]
    [InlineData("１２３４５６", HouseholdRole.Adult, false)]
    public void Pin_format_is_role_specific_and_ASCII_only(
        string value,
        HouseholdRole role,
        bool expected)
    {
        Assert.Equal(expected, PinPolicy.IsValid(value, role));
    }

    [Fact]
    public void Credential_transitions_to_ready_and_resets_failures()
    {
        var memberId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);
        var credential = new MemberCredential(memberId);
        credential.RecordFailure(now);
        credential.SetPin("versioned-password-hash", now.AddSeconds(1));

        Assert.Equal(CredentialState.Ready, credential.State);
        Assert.Equal("versioned-password-hash", credential.PinHash);
        Assert.Equal(0, credential.FailedAttemptCount);
        Assert.Null(credential.FailedWindowStartedAtUtc);
    }

    [Fact]
    public void Household_bootstrap_completes_once()
    {
        var bootstrap = new HouseholdBootstrap(HouseholdBootstrap.SingletonId);
        var adultId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        bootstrap.Complete(adultId, now);

        Assert.Equal(BootstrapState.Complete, bootstrap.State);
        Assert.Equal(adultId, bootstrap.FirstAdultId);
        Assert.Throws<InvalidOperationException>(() => bootstrap.Complete(Guid.NewGuid(), now));
    }

    [Fact]
    public void Session_expires_at_the_exact_inactivity_boundary()
    {
        var now = new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);
        var session = new AuthSession(
            Guid.NewGuid(),
            Guid.NewGuid(),
            HouseholdRole.Adult,
            "refresh-hash",
            now);

        Assert.True(session.IsActive(now.AddMinutes(10).AddTicks(-1)));
        Assert.False(session.IsActive(now.AddMinutes(10)));
    }

    [Fact]
    public void Pin_setup_token_is_single_use_and_expires_at_five_minutes()
    {
        var now = new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);
        var token = new PinSetupToken(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "setup-hash",
            now);

        Assert.True(token.CanConsume("setup-hash", now.AddMinutes(5).AddTicks(-1)));
        Assert.False(token.CanConsume("wrong-hash", now.AddMinutes(1)));
        Assert.False(token.CanConsume("setup-hash", now.AddMinutes(5)));

        token.Consume(now.AddMinutes(1));

        Assert.False(token.CanConsume("setup-hash", now.AddMinutes(2)));
    }
}
