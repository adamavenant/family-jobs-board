using FamilyJobsBoard.Api.Composition;
using FamilyJobsBoard.Api.Features.Administration;
using FamilyJobsBoard.Api.Features.Health;
using FamilyJobsBoard.Api.Features.Calendar;
using FamilyJobsBoard.Api.Features.Identity;
using FamilyJobsBoard.Api.Features.PointAdjustments;
using FamilyJobsBoard.Api.Features.PointRedemptions;
using FamilyJobsBoard.Api.Features.Points;
using FamilyJobsBoard.Api.Features.GoodBehaviours;
using FamilyJobsBoard.Api.Features.Today;
using FamilyJobsBoard.Api.Features.Telemetry;
using FamilyJobsBoard.Api.Features.TurnRotations;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.Configure(options => options.ActivityTrackingOptions = ActivityTrackingOptions.None);
builder.Logging.AddFilter(
    ApplicationEventMetricsLoggerProvider.ExceptionHandlerCategory,
    LogLevel.None);
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = false;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    options.UseUtcTimestamp = true;
});

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<OpenApiAuthenticationTransformer>();
    options.AddOperationTransformer<OpenApiAuthenticationTransformer>();
});
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddFamilyAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddApplicationTelemetry(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseMiddleware<RequestTelemetryMiddleware>();
app.UseExceptionHandler();
app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthEndpoints();
app.MapAdministrationEndpoints();
app.MapIdentityEndpoints();
app.MapTodayEndpoints();
app.MapGoodBehaviourEndpoints();
app.MapPointAdjustmentEndpoints();
app.MapPointRedemptionEndpoints();
app.MapPointsLedgerEndpoints();
app.MapCalendarEndpoints();
app.MapTurnRotationEndpoints();

app.Run();

public partial class Program;
