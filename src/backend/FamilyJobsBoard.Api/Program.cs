using FamilyJobsBoard.Api.Composition;
using FamilyJobsBoard.Api.Features.Health;
using FamilyJobsBoard.Api.Features.Today;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthEndpoints();
app.MapTodayEndpoints();

app.Run();

public partial class Program;
