using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using FamilyJobsBoard.Api.Features.Identity;
using FamilyJobsBoard.Application.Administration;
using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Application.Identity;
using FamilyJobsBoard.Application.GoodBehaviours;
using FamilyJobsBoard.Application.Calendar;
using FamilyJobsBoard.Application.PointAdjustments;
using FamilyJobsBoard.Application.Today;
using FamilyJobsBoard.Infrastructure.Data;
using FamilyJobsBoard.Infrastructure.Administration;
using FamilyJobsBoard.Infrastructure.Identity;
using FamilyJobsBoard.Infrastructure.Time;
using FamilyJobsBoard.Infrastructure.GoodBehaviours;
using FamilyJobsBoard.Infrastructure.PointAdjustments;
using FamilyJobsBoard.Infrastructure.Today;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace FamilyJobsBoard.Api.Composition;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AdministrationService>();
        services.AddScoped<TodayBoardService>();
        services.AddScoped<GoodBehaviourService>();
        services.AddScoped<PointAdjustmentService>();
        services.AddScoped<CalendarService>();
        services.AddScoped<IdentityService>();
        return services;
    }

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = GetDatabaseConnectionString(configuration, environment);
        var timeZoneId = configuration["Household:TimeZone"] ?? "Africa/Johannesburg";

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IAdministrationRepository, EfAdministrationRepository>();
        services.AddScoped<ITodayBoardRepository, EfTodayBoardRepository>();
        services.AddScoped<IGoodBehaviourRepository, EfGoodBehaviourRepository>();
        services.AddScoped<IPointAdjustmentRepository, EfPointAdjustmentRepository>();
        services.AddScoped<IIdentityRepository, EfIdentityRepository>();
        services.AddSingleton<IHouseholdClock>(new SystemHouseholdClock(timeZoneId));

        return services;
    }

    public static IServiceCollection AddFamilyAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = GetAuthenticationOptions(configuration, environment);
        services.AddSingleton(options);
        services.AddSingleton<IPinHasher, AspNetCorePinHasher>();
        services.AddSingleton<IIdentityTokenService, IdentityTokenService>();
        services.AddScoped<SessionValidationEvents>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.MapInboundClaims = false;
                jwt.EventsType = typeof(SessionValidationEvents);
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(options.JwtSigningKey),
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = "sub",
                    RoleClaimType = ClaimTypes.Role,
                };
            });
        services.AddAuthorizationBuilder()
            .AddPolicy("Adult", policy => policy.RequireRole("Adult"))
            .AddPolicy("Child", policy => policy.RequireRole("Child"));
        services.AddRateLimiter(rateLimiter =>
        {
            rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiter.AddPolicy("sign-in", context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(5),
                        SegmentsPerWindow = 5,
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
            rateLimiter.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/problem+json";
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        type = "about:blank",
                        title = "Try again later",
                        status = 429,
                        detail = "Try again later.",
                        code = "try_again_later",
                    },
                    cancellationToken);
            };
        });
        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            forwarded.ForwardLimit = 1;
            forwarded.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
        });
        return services;
    }

    internal static string GetDatabaseConnectionString(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("Database");

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Database is required when the API runs in Production.");
        }

        return "Host=localhost;Port=5432;Database=family_jobs_board;Username=family_jobs_board;Password=family_jobs_board";
    }

    internal static FamilyAuthenticationOptions GetAuthenticationOptions(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var developmentSecret = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            "development-only-secret-key-32-bytes"));
        var pepperValue = configuration["Authentication:PinPepper"];
        var jwtValue = configuration["Authentication:JwtSigningKey"];
        var allowedOrigin = configuration["Authentication:AllowedOrigin"];
        if (!environment.IsProduction())
        {
            pepperValue ??= developmentSecret;
            jwtValue ??= developmentSecret;
            allowedOrigin ??= "http://localhost:3000";
        }

        var pepper = DecodeSecret(pepperValue, "Authentication:PinPepper");
        var jwtKey = DecodeSecret(jwtValue, "Authentication:JwtSigningKey");
        if (string.IsNullOrWhiteSpace(allowedOrigin)
            || !Uri.TryCreate(allowedOrigin, UriKind.Absolute, out var origin)
            || origin.GetLeftPart(UriPartial.Authority) != allowedOrigin.TrimEnd('/'))
        {
            throw new InvalidOperationException(
                "Authentication:AllowedOrigin must be one absolute origin without a path.");
        }

        var iterations = configuration.GetValue<int?>("Authentication:PinHashIterations") ?? 220_000;
        if (iterations < 220_000)
        {
            throw new InvalidOperationException(
                "Authentication:PinHashIterations must be at least 220000.");
        }

        return new FamilyAuthenticationOptions(
            pepper,
            jwtKey,
            configuration["Authentication:Issuer"] ?? "family-jobs-board",
            configuration["Authentication:Audience"] ?? "family-jobs-board-api",
            allowedOrigin.TrimEnd('/'),
            iterations);
    }

    private static byte[] DecodeSecret(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required in Production.");
        }

        try
        {
            var bytes = Convert.FromBase64String(value);
            return bytes.Length >= 32
                ? bytes
                : throw new InvalidOperationException($"{name} must contain at least 32 random bytes.");
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"{name} must be base64 encoded.", exception);
        }
    }
}
