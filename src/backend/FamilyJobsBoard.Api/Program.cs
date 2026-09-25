using FamilyJobsBoard.Api.Composition;
using FamilyJobsBoard.Api.Features.Administration;
using FamilyJobsBoard.Api.Features.Health;
using FamilyJobsBoard.Api.Features.Identity;
using FamilyJobsBoard.Api.Features.PointAdjustments;
using FamilyJobsBoard.Api.Features.GoodBehaviours;
using FamilyJobsBoard.Api.Features.Today;
using FamilyJobsBoard.Api.Features.TurnRotations;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<OpenApiAuthenticationTransformer>();
    options.AddOperationTransformer<OpenApiAuthenticationTransformer>();
});
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddFamilyAuthentication(builder.Configuration, builder.Environment);

var app = builder.Build();

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
app.MapTurnRotationEndpoints();

app.Run();

public partial class Program;
