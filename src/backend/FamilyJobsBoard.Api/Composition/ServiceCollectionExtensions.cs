using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Application.Today;
using FamilyJobsBoard.Infrastructure.Data;
using FamilyJobsBoard.Infrastructure.Time;
using FamilyJobsBoard.Infrastructure.Today;
using Microsoft.EntityFrameworkCore;

namespace FamilyJobsBoard.Api.Composition;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<TodayBoardService>();
        return services;
    }

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? "Host=localhost;Port=5432;Database=family_jobs_board;Username=family_jobs_board;Password=family_jobs_board";
        var timeZoneId = configuration["Household:TimeZone"] ?? "Africa/Johannesburg";

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ITodayBoardRepository, EfTodayBoardRepository>();
        services.AddSingleton<IHouseholdClock>(new SystemHouseholdClock(timeZoneId));

        return services;
    }
}
