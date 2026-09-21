using FamilyJobsBoard.Domain.GoodBehaviours;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Identity;
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
        await AddCredentialIfMissingAsync(DemoDataIds.Addie, cancellationToken);
        await AddCredentialIfMissingAsync(DemoDataIds.Hellie, cancellationToken);
        await AddCredentialIfMissingAsync(DemoDataIds.Fredster, cancellationToken);
        await AddCredentialIfMissingAsync(DemoDataIds.Harrie, cancellationToken);

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

        await AddGoodBehaviourTypeIfMissingAsync(
            DemoDataIds.ShowingKindness,
            "Showing Kindness",
            "Did something kind for someone else.",
            5,
            cancellationToken);
        await AddGoodBehaviourTypeIfMissingAsync(
            DemoDataIds.BeingHelpful,
            "Being Helpful",
            "Helped out without being asked.",
            5,
            cancellationToken);
        await AddGoodBehaviourTypeIfMissingAsync(
            DemoDataIds.BeingBrave,
            "Being Brave",
            "Tried something that felt scary or hard.",
            10,
            cancellationToken);

        await _database.SaveChangesAsync(cancellationToken);
    }

    private async Task AddGoodBehaviourTypeIfMissingAsync(
        Guid id,
        string name,
        string description,
        int points,
        CancellationToken cancellationToken)
    {
        if (!await _database.GoodBehaviourTypes.AnyAsync(type => type.Id == id, cancellationToken))
        {
            _database.GoodBehaviourTypes.Add(new GoodBehaviourType(
                id,
                name,
                description,
                points,
                DemoDataIds.Addie,
                DateTimeOffset.UtcNow));
        }
    }

    private async Task AddCredentialIfMissingAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        if (!await _database.MemberCredentials.AnyAsync(
                credential => credential.MemberId == memberId,
                cancellationToken))
        {
            _database.MemberCredentials.Add(new MemberCredential(memberId));
        }
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
