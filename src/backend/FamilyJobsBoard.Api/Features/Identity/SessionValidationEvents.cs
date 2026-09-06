using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Application.Identity;
using FamilyJobsBoard.Domain.Households;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace FamilyJobsBoard.Api.Features.Identity;

internal sealed class SessionValidationEvents : JwtBearerEvents
{
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var subject = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var session = context.Principal?.FindFirstValue("sid");
        var roleValue = context.Principal?.FindFirstValue(ClaimTypes.Role);
        var tokenId = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Jti);
        var issuedAt = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Iat);
        if (!Guid.TryParse(subject, out var memberId)
            || !Guid.TryParse(session, out var sessionId)
            || !Enum.TryParse<HouseholdRole>(roleValue, out var role)
            || !Guid.TryParse(tokenId, out _)
            || !long.TryParse(issuedAt, out var issuedAtSeconds))
        {
            context.Fail("Required identity claims are invalid.");
            return;
        }

        var service = context.HttpContext.RequestServices.GetRequiredService<IdentityService>();
        var clock = context.HttpContext.RequestServices.GetRequiredService<IHouseholdClock>();
        if (DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds) > clock.UtcNow.AddSeconds(30))
        {
            context.Fail("The token issue time is invalid.");
            return;
        }

        if (!await service.ValidateSessionAsync(
                sessionId,
                memberId,
                role,
                context.HttpContext.RequestAborted))
        {
            context.Fail("The session is no longer active.");
        }
    }
}
