using FamilyJobsBoard.Infrastructure.Data;
using FamilyJobsBoard.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Database")
    ?? throw new InvalidOperationException("ConnectionStrings__Database is required.");
var timeZoneId = Environment.GetEnvironmentVariable("Household__TimeZone") ?? "Africa/Johannesburg";

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(connectionString)
    .Options;

await using var database = new AppDbContext(options);
await database.Database.MigrateAsync();

var clock = new SystemHouseholdClock(timeZoneId);
var seeder = new DemoDataSeeder(database);
await seeder.SeedAsync(clock.Today, CancellationToken.None);

Console.WriteLine("Database migrated and deterministic demo data is ready.");
