namespace FamilyJobsBoard.Api.Features.Identity;

internal sealed record BootstrapRequest(
    string Mode,
    Guid? MemberId,
    string? FirstName,
    string? Surname,
    string? Pin);

internal sealed record SignInRequest(Guid MemberId, string? Pin);

internal sealed record SetupPinRequest(string? SetupToken, string? Pin);

internal sealed record StartPinSetupRequest(string? Surname);

internal sealed record AuthMemberResponse(
    Guid Id,
    string DisplayName,
    string Role);

internal sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    AuthMemberResponse Member);

internal sealed record StartMemberResponse(
    Guid Id,
    string DisplayName,
    string Role,
    bool RequiresSurname);

internal sealed record AuthStartResponse(
    string State,
    IReadOnlyList<StartMemberResponse>? Adults = null,
    IReadOnlyList<StartMemberResponse>? Members = null);

internal sealed record PinSetupResponse(
    string SetupToken,
    DateTimeOffset ExpiresAtUtc,
    string TargetDisplayName,
    string TargetRole);
