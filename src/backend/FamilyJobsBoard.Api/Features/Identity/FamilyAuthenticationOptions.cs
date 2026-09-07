namespace FamilyJobsBoard.Api.Features.Identity;

internal sealed record FamilyAuthenticationOptions(
    byte[] PinPepper,
    byte[] JwtSigningKey,
    string Issuer,
    string Audience,
    string AllowedOrigin,
    int PinHashIterations);
