using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FamilyJobsBoard.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyJobsBoard.Api.Features.Identity;

internal static class IdentityEndpoints
{
    internal const string RefreshCookieName = "family_jobs_board_refresh";

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth").WithTags("Identity");

        group.MapGet("/start", GetStartAsync)
            .AllowAnonymous()
            .Produces<AuthStartResponse>()
            .WithName("GetAuthenticationStart")
            .WithSummary("Get the household bootstrap or sign-in state.");
        group.MapPost("/bootstrap", BootstrapAsync)
            .AllowAnonymous()
            .Produces<AuthResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("BootstrapHousehold")
            .WithSummary("Create or claim the first household adult.");
        group.MapPost("/sign-in", SignInAsync)
            .AllowAnonymous()
            .RequireRateLimiting("sign-in")
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithName("SignIn")
            .WithSummary("Sign in a selected household member with a PIN.");
        group.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("RefreshSession")
            .WithSummary("Rotate a same-origin refresh token and renew access.");
        group.MapPost("/logout", LogoutAsync)
            .AllowAnonymous()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("Logout")
            .WithSummary("Revoke the current session and clear its cookie.");
        group.MapPost("/setup-pin", SetupPinAsync)
            .AllowAnonymous()
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("SetupPin")
            .WithSummary("Consume a one-time handoff and set the target member PIN.");

        var users = endpoints.MapGroup("/api/users")
            .WithTags("Users")
            .RequireAuthorization("Adult");
        users.MapGet("", GetMembersAsync)
            .Produces<IReadOnlyList<FamilyMemberResponse>>()
            .WithName("GetFamilyMembers")
            .WithSummary("List active household members for administration.");
        users.MapPost("", CreateMemberAsync)
            .Produces<FamilyMemberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("CreateFamilyMember")
            .WithSummary("Create an active household member awaiting PIN setup.");
        users.MapPost("/{memberId:guid}/pin-setup", StartPinSetupAsync)
            .Produces<PinSetupResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("StartPinSetup")
            .WithSummary("Authorize a one-time PIN handoff to an unconfigured member.");

        return endpoints;
    }

    private static async Task<IResult> GetMembersAsync(
        IdentityService service,
        CancellationToken cancellationToken)
    {
        var members = await service.GetActiveMembersAsync(cancellationToken);
        return TypedResults.Ok(members.Select(MapFamilyMember).ToArray());
    }

    private static async Task<IResult> CreateMemberAsync(
        CreateFamilyMemberRequest request,
        HttpContext context,
        IdentityService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var member = await service.CreateMemberAsync(
                new CreateFamilyMember(
                    request.FirstName,
                    request.Surname,
                    request.Nickname,
                    request.Role),
                cancellationToken);
            loggerFactory.CreateLogger("IdentityAudit").LogInformation(
                "FamilyMemberCreated ActorId={ActorId} TargetId={TargetId} Role={Role}",
                PrincipalMemberId(context.User),
                member.Id,
                member.Role);
            return TypedResults.Created($"/api/users/{member.Id}", MapFamilyMember(member));
        }
        catch (IdentityOperationException exception)
        {
            return MapError(exception.Error);
        }
    }

    private static async Task<IResult> GetStartAsync(
        IdentityService service,
        CancellationToken cancellationToken)
    {
        var start = await service.GetStartAsync(cancellationToken);
        var members = start.Members.Select(MapStartMember).ToArray();
        return start.Mode switch
        {
            IdentityStartMode.CreateFirstAdult => TypedResults.Ok(
                new AuthStartResponse("createFirstAdult")),
            IdentityStartMode.ClaimExistingAdult => TypedResults.Ok(
                new AuthStartResponse("claimExistingAdult", Adults: members)),
            _ => TypedResults.Ok(new AuthStartResponse("signIn", Members: members)),
        };
    }

    private static async Task<IResult> BootstrapAsync(
        BootstrapRequest request,
        HttpContext context,
        IdentityService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.BootstrapAsync(
                new BootstrapIdentity(
                    request.Mode,
                    request.MemberId,
                    request.FirstName,
                    request.Surname,
                    request.Pin),
                cancellationToken);
            SetRefreshCookie(context, result.RefreshToken);
            loggerFactory.CreateLogger("IdentityAudit").LogInformation(
                "HouseholdBootstrapSucceeded MemberId={MemberId} SessionId={SessionId}",
                result.Member.Id,
                result.SessionId);
            return TypedResults.Created("/api/today", MapAuth(result));
        }
        catch (IdentityOperationException exception)
        {
            return MapError(exception.Error);
        }
    }

    private static async Task<IResult> SignInAsync(
        SignInRequest request,
        HttpContext context,
        IdentityService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.SignInAsync(request.MemberId, request.Pin, cancellationToken);
            SetRefreshCookie(context, result.RefreshToken);
            loggerFactory.CreateLogger("IdentityAudit").LogInformation(
                "SignInSucceeded MemberId={MemberId} SessionId={SessionId}",
                result.Member.Id,
                result.SessionId);
            return TypedResults.Ok(MapAuth(result));
        }
        catch (IdentityOperationException exception)
        {
            loggerFactory.CreateLogger("IdentityAudit").LogWarning(
                "SignInRejected MemberId={MemberId} Outcome={Outcome}",
                request.MemberId,
                exception.Error);
            return MapError(exception.Error);
        }
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext context,
        IdentityService service,
        FamilyAuthenticationOptions options,
        CancellationToken cancellationToken)
    {
        if (!HasExpectedOrigin(context, options))
        {
            ClearRefreshCookie(context);
            return Problem(StatusCodes.Status403Forbidden, "origin_rejected", "Request origin rejected.");
        }

        try
        {
            context.Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken);
            var result = await service.RefreshAsync(refreshToken, cancellationToken);
            SetRefreshCookie(context, result.RefreshToken);
            return TypedResults.Ok(MapAuth(result));
        }
        catch (IdentityOperationException exception)
        {
            ClearRefreshCookie(context);
            return MapError(exception.Error);
        }
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        IdentityService service,
        IIdentityTokenService tokens,
        FamilyAuthenticationOptions options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!HasExpectedOrigin(context, options))
        {
            ClearRefreshCookie(context);
            return Problem(StatusCodes.Status403Forbidden, "origin_rejected", "Request origin rejected.");
        }

        var sessionId = PrincipalSessionId(context.User);
        if (sessionId is null
            && context.Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken)
            && tokens.TryReadOpaqueTokenId(refreshToken, out var cookieSessionId))
        {
            sessionId = cookieSessionId;
        }

        if (sessionId is not null)
        {
            await service.LogoutAsync(sessionId.Value, cancellationToken);
            loggerFactory.CreateLogger("IdentityAudit").LogInformation(
                "LogoutSucceeded SessionId={SessionId}",
                sessionId);
        }

        ClearRefreshCookie(context);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> StartPinSetupAsync(
        Guid memberId,
        StartPinSetupRequest request,
        HttpContext context,
        IdentityService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var adultId = PrincipalMemberId(context.User);
        var adultSessionId = PrincipalSessionId(context.User);
        if (adultId is null || adultSessionId is null)
        {
            return TypedResults.Unauthorized();
        }

        try
        {
            var grant = await service.IssuePinSetupAsync(
                memberId,
                adultId.Value,
                adultSessionId.Value,
                request.Surname,
                cancellationToken);
            ClearRefreshCookie(context);
            loggerFactory.CreateLogger("IdentityAudit").LogInformation(
                "PinSetupIssued ActorId={ActorId} TargetId={TargetId}",
                adultId,
                memberId);
            return TypedResults.Ok(new PinSetupResponse(
                grant.SetupToken,
                grant.ExpiresAtUtc,
                grant.Member.DisplayName,
                MapRole(grant.Member.Role)));
        }
        catch (IdentityOperationException exception)
        {
            return MapError(exception.Error);
        }
    }

    private static async Task<IResult> SetupPinAsync(
        SetupPinRequest request,
        HttpContext context,
        IdentityService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.SetupPinAsync(request.SetupToken, request.Pin, cancellationToken);
            SetRefreshCookie(context, result.RefreshToken);
            loggerFactory.CreateLogger("IdentityAudit").LogInformation(
                "PinSetupConsumed TargetId={TargetId} SessionId={SessionId}",
                result.Member.Id,
                result.SessionId);
            return TypedResults.Ok(MapAuth(result));
        }
        catch (IdentityOperationException exception)
        {
            return MapError(exception.Error);
        }
    }

    private static AuthResponse MapAuth(AuthenticatedIdentity identity) =>
        new(
            identity.AccessToken,
            identity.AccessTokenExpiresAtUtc,
            new AuthMemberResponse(
                identity.Member.Id,
                identity.Member.DisplayName,
                MapRole(identity.Member.Role)));

    private static StartMemberResponse MapStartMember(IdentityMember member) =>
        new(
            member.Id,
            member.DisplayName,
            MapRole(member.Role),
            member.Surname is null);

    private static FamilyMemberResponse MapFamilyMember(IdentityMember member) =>
        new(
            member.Id,
            member.FirstName,
            member.Surname,
            member.Nickname,
            member.DisplayName,
            MapRole(member.Role),
            member.IsCredentialReady);

    private static string MapRole(Domain.Households.HouseholdRole role) =>
        role == Domain.Households.HouseholdRole.Adult ? "adult" : "child";

    internal static Guid? PrincipalMemberId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

    internal static Guid? PrincipalSessionId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sid"), out var id) ? id : null;

    private static bool HasExpectedOrigin(HttpContext context, FamilyAuthenticationOptions options) =>
        string.Equals(
            context.Request.Headers.Origin.ToString(),
            options.AllowedOrigin,
            StringComparison.OrdinalIgnoreCase);

    private static void SetRefreshCookie(HttpContext context, string value)
    {
        context.Response.Cookies.Append(
            RefreshCookieName,
            value,
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps,
                Path = "/api/auth",
                MaxAge = TimeSpan.FromMinutes(10),
                IsEssential = true,
            });
    }

    private static void ClearRefreshCookie(HttpContext context)
    {
        context.Response.Cookies.Delete(
            RefreshCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps,
                Path = "/api/auth",
                IsEssential = true,
            });
    }

    private static IResult MapError(IdentityError error) => error switch
    {
        IdentityError.InvalidBootstrap => Problem(400, "invalid_bootstrap", "Bootstrap details are invalid."),
        IdentityError.HouseholdAlreadyBootstrapped => Problem(409, "household_already_bootstrapped", "The household is already configured."),
        IdentityError.AdultNotClaimable => Problem(409, "adult_not_claimable", "That adult cannot be claimed."),
        IdentityError.InvalidCredentials => Problem(401, "invalid_credentials", "The member or PIN was not accepted."),
        IdentityError.TryAgainLater => Problem(429, "try_again_later", "Try again later.", retryAfterSeconds: 60),
        IdentityError.SessionExpired => Problem(401, "session_expired", "The session has expired."),
        IdentityError.MemberNotFound => Problem(404, "member_not_found", "The member was not found."),
        IdentityError.PinAlreadySet => Problem(409, "pin_already_set", "That member already has a PIN."),
        IdentityError.MemberNotEligible => Problem(409, "member_not_eligible", "That member is not eligible for PIN setup."),
        IdentityError.InvalidMember => Problem(400, "invalid_member", "Check the family member details."),
        _ => Problem(400, "invalid_or_expired_setup", "The setup request is invalid or expired."),
    };

    private static IResult Problem(
        int status,
        string code,
        string detail,
        int? retryAfterSeconds = null)
    {
        var result = TypedResults.Problem(
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code });
        return retryAfterSeconds is null
            ? result
            : new RetryAfterResult(result, retryAfterSeconds.Value);
    }

    private sealed class RetryAfterResult(IResult inner, int seconds) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.RetryAfter = seconds.ToString();
            return inner.ExecuteAsync(httpContext);
        }
    }
}
