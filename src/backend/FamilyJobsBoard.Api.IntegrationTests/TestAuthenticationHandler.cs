using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FamilyJobsBoard.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyJobsBoard.Api.IntegrationTests;

internal sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "IntegrationTest";

    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var memberId = ResolveMemberId();
        var role = memberId is var id && (id == DemoDataIds.Addie || id == DemoDataIds.Hellie)
            ? "Adult"
            : "Child";
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, memberId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim("sid", Guid.Parse("4ca48ea9-5d39-490d-b0d9-7e086d3dedc3").ToString()),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, SchemeName)));
    }

    private Guid ResolveMemberId()
    {
        if (Request.Headers.TryGetValue("X-Test-Member-Id", out var header)
            && Guid.TryParse(header, out var headerId))
        {
            return headerId;
        }

        if (HttpMethods.IsGet(Request.Method)
            && Request.Query.TryGetValue("memberId", out var query)
            && Guid.TryParse(query, out var queryId))
        {
            return queryId;
        }

        return HttpMethods.IsGet(Request.Method)
            || Request.Path.Value?.EndsWith("/complete", StringComparison.Ordinal) == true
                ? DemoDataIds.Fredster
                : DemoDataIds.Addie;
    }
}
