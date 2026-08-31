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
        if (!await _database.HouseholdMembers.AnyAsync(
                member => member.Id == DemoDataIds.Adult,
                cancellationToken))
        {
            _database.HouseholdMembers.Add(new HouseholdMember(DemoDataIds.Adult, "Adam", true));
        }

        if (!await _database.HouseholdMembers.AnyAsync(
                member => member.Id == DemoDataIds.Child,
                cancellationToken))
        {
            _database.HouseholdMembers.Add(new HouseholdMember(DemoDataIds.Child, "Addie", false));
        }

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
                new Job(id, DemoDataIds.Child, name, description, points, householdDate));
            return;
        }

        existing.ScheduleFor(householdDate);
    }
}
