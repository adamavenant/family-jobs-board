using FamilyJobsBoard.Api.Composition;
using FamilyJobsBoard.Api.Features.Health;
using FamilyJobsBoard.Api.Features.Identity;
using FamilyJobsBoard.Api.Features.Today;

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
app.MapIdentityEndpoints();
app.MapTodayEndpoints();

app.Run();

public partial class Program;
