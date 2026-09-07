using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Application.Today;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Domain.Points;
using Xunit;

namespace FamilyJobsBoard.Application.Tests;

public sealed class TodayBoardServiceTests
{
    private static readonly DateOnly Today = new(2026, 9, 7);
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
    public async Task One_off_job_creates_an_independent_copy_for_each_child()
    {
        var repository = new RecordingRepository([Adult, FirstChild, SecondChild]);
        var service = new TodayBoardService(repository, new FixedClock());

        var created = await service.AddJobAsync(
            new AddTodayJob(
                [FirstChild.Id, SecondChild.Id],
                "  Make the beds  ",
                "  Straighten the duvets.  ",
                3),
            CancellationToken.None);

        Assert.Equal(2, created.Count);
        Assert.Equal(2, created.Select(job => job.Id).Distinct().Count());
        Assert.Equal(
            new[] { FirstChild.Id, SecondChild.Id }.Order(),
            created.Select(job => job.ChildId).Order());
        Assert.All(created, job =>
        {
            Assert.Equal("Make the beds", job.Name);
            Assert.Equal("Straighten the duvets.", job.Description);
            Assert.Equal(3, job.Points);
        });
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Invalid_assignee_set_is_rejected_before_any_write()
    {
        var inactiveChild = new HouseholdMember(
            Guid.NewGuid(),
            "Inactive",
            "Child",
            HouseholdRole.Child,
            isActive: false);
        var repository = new RecordingRepository([Adult, FirstChild, inactiveChild]);
        var service = new TodayBoardService(repository, new FixedClock());

        var exception = await Assert.ThrowsAsync<InvalidTodayJobException>(() =>
            service.AddJobAsync(
                new AddTodayJob([FirstChild.Id, inactiveChild.Id], "A job", "", 1),
                CancellationToken.None));

        Assert.Contains("ChildIds", exception.Errors.Keys);
        Assert.Empty(repository.Jobs);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task Recurring_assignment_for_two_children_is_idempotent()
    {
        var repository = new RecordingRepository([Adult, FirstChild, SecondChild]);
        var service = new TodayBoardService(repository, new FixedClock());
        var requestId = Guid.NewGuid();
        var request = new CreateDailyRecurringJob(
            requestId,
            Adult.Id,
            [FirstChild.Id, SecondChild.Id],
            "Feed the fish",
            "One scoop.",
            2,
            "morning",
            null,
            Today,
            Today.AddDays(1));

        var created = await service.CreateDailyRecurringJobAsync(
            request,
            CancellationToken.None);
        var retried = await service.CreateDailyRecurringJobAsync(
            request,
            CancellationToken.None);

        Assert.True(created.WasCreated);
        Assert.False(retried.WasCreated);
        Assert.Equal(2, created.Assignments.Count);
        Assert.Equal(
            created.Assignments.Select(assignment => assignment.SeriesId).Order(),
            retried.Assignments.Select(assignment => assignment.SeriesId).Order());
        Assert.Equal(2, repository.Series.Count);
        Assert.Equal(4, repository.Jobs.Count);
        Assert.Equal(1, repository.SaveCount);
        Assert.All(repository.Series, series => Assert.Equal(requestId, series.AssignmentRequestId));
    }

    private sealed class FixedClock : IHouseholdClock
    {
        public DateOnly Today => TodayBoardServiceTests.Today;

        public DateTimeOffset UtcNow => new(2026, 9, 7, 8, 0, 0, TimeSpan.Zero);
    }

    private sealed class RecordingRepository : ITodayBoardRepository
    {
        private readonly IReadOnlyList<HouseholdMember> _members;

        public RecordingRepository(IReadOnlyList<HouseholdMember> members)
        {
            _members = members;
        }

        public List<Job> Jobs { get; } = [];

        public List<RecurringJobSeries> Series { get; } = [];

        public int SaveCount { get; private set; }

        public Task<IReadOnlyList<HouseholdMember>> GetMembersAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(_members);

        public Task<HouseholdMember?> GetMemberAsync(
            Guid memberId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_members.SingleOrDefault(member => member.Id == memberId));

        public Task AddJobsAsync(
            IReadOnlyCollection<Job> jobs,
            CancellationToken cancellationToken)
        {
            Jobs.AddRange(jobs);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RecurringJobSeries>> GetRecurringJobSeriesByRequestAsync(
            Guid assignmentRequestId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RecurringJobSeries>>(
                Series.Where(item => item.AssignmentRequestId == assignmentRequestId).ToArray());

        public Task AddRecurringJobSeriesAsync(
            IReadOnlyCollection<RecurringJobSeries> series,
            CancellationToken cancellationToken)
        {
            Series.AddRange(series);
            return Task.CompletedTask;
        }

        public Task<int> GetRecurringJobSeriesOccurrenceCountAsync(
            Guid seriesId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Jobs.Count(job => job.RecurringJobSeriesId == seriesId));

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Job>> GetJobsAsync(IReadOnlyCollection<Guid> childIds, DateOnly scheduledDate, CancellationToken cancellationToken) => throw Unused();
        public Task<Job?> GetJobAsync(Guid jobId, CancellationToken cancellationToken) => throw Unused();
        public Task<IReadOnlyList<TodayJobRejection>> GetLatestRejectionsAsync(IReadOnlyCollection<Guid> childIds, DateOnly scheduledDate, CancellationToken cancellationToken) => throw Unused();
        public Task<IReadOnlyList<RecurringJobSeries>> GetRecurringJobSeriesNeedingGenerationAsync(DateOnly horizon, CancellationToken cancellationToken) => throw Unused();
        public Task<TodayPointsSummary> GetPointsSummaryAsync(Guid childId, CancellationToken cancellationToken) => throw Unused();
        public Task AddPointsAwardAsync(PointsLedgerEntry entry, CancellationToken cancellationToken) => throw Unused();
        public Task AddReviewDecisionAsync(JobReviewDecision decision, CancellationToken cancellationToken) => throw Unused();

        private static NotSupportedException Unused() => new("This operation is not used by the test.");
    }
}
