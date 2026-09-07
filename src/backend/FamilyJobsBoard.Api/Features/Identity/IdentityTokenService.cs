using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FamilyJobsBoard.Application.Identity;
using Microsoft.IdentityModel.Tokens;

namespace FamilyJobsBoard.Api.Features.Identity;

internal sealed class IdentityTokenService : IIdentityTokenService
{
    private readonly FamilyAuthenticationOptions _options;

    public IdentityTokenService(FamilyAuthenticationOptions options)
    {
        _options = options;
    }

    public GeneratedOpaqueToken CreateOpaqueToken(Guid id)
    {
        var random = RandomNumberGenerator.GetBytes(32);
        var value = $"{id:N}.{Base64Url(random)}";
        return new GeneratedOpaqueToken(id, value, HashOpaqueToken(value));
    }

    public bool TryReadOpaqueTokenId(string value, out Guid id)
    {
        id = Guid.Empty;
        var separator = value.IndexOf('.', StringComparison.Ordinal);
        return separator == 32
            && value.IndexOf('.', separator + 1) < 0
            && Guid.TryParseExact(value.AsSpan(0, separator), "N", out id);
    }

    public string HashOpaqueToken(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public IssuedAccessToken IssueAccessToken(
        IdentityMember member,
        Guid sessionId,
        DateTimeOffset now)
    {
        var expires = now.AddMinutes(5);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, member.Id.ToString()),
            new Claim(ClaimTypes.Role, member.Role.ToString()),
            new Claim("sid", sessionId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(
                JwtRegisteredClaimNames.Iat,
                now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(_options.JwtSigningKey),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            now.UtcDateTime,
            expires.UtcDateTime,
            credentials);
        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
