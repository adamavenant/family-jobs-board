using FamilyJobsBoard.Application.Approvals;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using Xunit;

namespace FamilyJobsBoard.Application.Tests;

public sealed class PendingApprovalsServiceTests
{
    private static readonly DateOnly Today = new(2026, 9, 22);
    private static readonly DateTimeOffset SubmittedAt = new(2026, 9, 22, 7, 0, 0, TimeSpan.Zero);
    private static readonly HouseholdMember Adult = new(
        Guid.Parse("d55b77d7-a514-4071-a004-f5033aff2f9d"),
        "Addie",
        "Avenant",
        HouseholdRole.Adult);
    private static readonly HouseholdMember FirstChild = new(
        Guid.Parse("96e7d927-dfb6-480c-89be-a5c58144f603"),
        "Fredster",
        "Avenant",
        HouseholdRole.Child);
    private static readonly HouseholdMember SecondChild = new(
        Guid.Parse("3fdb1469-df09-420e-8eeb-330717c710fe"),
        "Harrie",
        "Avenant",
        HouseholdRole.Child);

    [Fact]
    public async Task Lists_every_pending_job_across_dates_and_children_oldest_first()
    {
        var repository = new CalendarServiceTests.FakeRepository([Adult, FirstChild, SecondChild]);
        repository.Jobs.AddRange(
        [
            Pending(SecondChild, "Today's", Today),
            Pending(FirstChild, "Last week's", Today.AddDays(-7)),
            new Job(Guid.NewGuid(), FirstChild.Id, "Still open", "", 1, Today.AddDays(-3)),
            Pending(FirstChild, "Yesterday's", Today.AddDays(-1)),
        ]);
        var service = new PendingApprovalsService(repository);

        var approvals = await service.GetAsync(CancellationToken.None);

        Assert.Equal(
            ["Last week's", "Yesterday's", "Today's"],
            approvals.Jobs.Select(job => job.Name));
        Assert.All(approvals.Jobs, job => Assert.Equal("pendingApproval", job.Status));
        Assert.Equal("Harrie", approvals.Jobs[^1].ChildDisplayName);
    }

    [Fact]
    public async Task Is_empty_when_nothing_awaits_review()
    {
        var repository = new CalendarServiceTests.FakeRepository([Adult, FirstChild]);
        repository.Jobs.Add(new Job(Guid.NewGuid(), FirstChild.Id, "Open", "", 1, Today));
        var service = new PendingApprovalsService(repository);

        var approvals = await service.GetAsync(CancellationToken.None);

        Assert.Empty(approvals.Jobs);
    }

    private static Job Pending(HouseholdMember child, string name, DateOnly scheduledDate)
    {
        var job = new Job(Guid.NewGuid(), child.Id, name, "", 2, scheduledDate);
        job.MarkComplete(SubmittedAt);
        return job;
    }
}
