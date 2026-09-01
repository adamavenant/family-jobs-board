using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using Microsoft.EntityFrameworkCore;

namespace FamilyJobsBoard.Infrastructure.Data;

public sealed class DemoDataSeeder
{
    private readonly AppDbContext _database;

    public DemoDataSeeder(AppDbContext database)
    {
        _database = database;
    }

    public async Task SeedAsync(DateOnly householdDate, CancellationToken cancellationToken)
    {
        await AddMemberIfMissingAsync(DemoDataIds.Addie, "Addie", true, cancellationToken);
        await AddMemberIfMissingAsync(DemoDataIds.Hellie, "Hellie", true, cancellationToken);
        await AddMemberIfMissingAsync(DemoDataIds.Fredster, "Fredster", false, cancellationToken);
        await AddMemberIfMissingAsync(DemoDataIds.Harrie, "Harrie", false, cancellationToken);

        await UpsertJobAsync(
            DemoDataIds.FeedDog,
            "Feed the dog",
            "Fill the food bowl and make sure there is fresh water.",
            5,
            householdDate,
            cancellationToken);
        await UpsertJobAsync(
            DemoDataIds.PackBag,
            "Pack school bag",
            "Check tomorrow's timetable and pack everything needed.",
            8,
            householdDate,
            cancellationToken);
        await UpsertJobAsync(
            DemoDataIds.ClearTable,
            "Clear the table",
            "Take dishes to the kitchen after dinner.",
            5,
            householdDate,
            cancellationToken);

        await _database.SaveChangesAsync(cancellationToken);
    }

    private async Task AddMemberIfMissingAsync(
        Guid id,
        string firstName,
        bool isAdult,
        CancellationToken cancellationToken)
    {
        if (!await _database.HouseholdMembers.AnyAsync(
                member => member.Id == id,
                cancellationToken))
        {
            _database.HouseholdMembers.Add(new HouseholdMember(id, firstName, isAdult));
        }
    }

    private async Task UpsertJobAsync(
        Guid id,
        string name,
        string description,
        int points,
        DateOnly householdDate,
        CancellationToken cancellationToken)
    {
        var existing = await _database.Jobs.SingleOrDefaultAsync(job => job.Id == id, cancellationToken);
        if (existing is null)
        {
            _database.Jobs.Add(
                new Job(id, DemoDataIds.Fredster, name, description, points, householdDate));
            return;
        }

        existing.ScheduleFor(householdDate);
    }
}
