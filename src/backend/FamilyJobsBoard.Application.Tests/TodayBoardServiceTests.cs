using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Application.Today;
using FamilyJobsBoard.Application.TurnRotations;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Domain.Points;
using FamilyJobsBoard.Domain.TurnRotations;
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
        var service = CreateService(repository);

        var created = await service.AddJobAsync(
            new AddTodayJob(
                [FirstChild.Id, SecondChild.Id],
                "  Make the beds  ",
                "  Straighten the duvets.  ",
                3,
                Today.AddDays(2),
                "evening",
                new TimeOnly(18, 30)),
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
            Assert.Equal(Today.AddDays(2), job.ScheduledDate);
            Assert.Equal("evening", job.AgendaPeriod);
            Assert.Equal(new TimeOnly(18, 30), job.ScheduledTime);
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
        var service = CreateService(repository);

        var exception = await Assert.ThrowsAsync<InvalidTodayJobException>(() =>
            service.AddJobAsync(
                new AddTodayJob(
                    [FirstChild.Id, inactiveChild.Id],
                    "A job",
                    "",
                    1,
                    Today,
                    "morning",
                    null),
                CancellationToken.None));

        Assert.Contains("ChildIds", exception.Errors.Keys);
        Assert.Empty(repository.Jobs);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task Past_date_and_invalid_agenda_period_are_rejected_before_any_write()
    {
        var repository = new RecordingRepository([Adult, FirstChild]);
        var service = CreateService(repository);

        var exception = await Assert.ThrowsAsync<InvalidTodayJobException>(() =>
            service.AddJobAsync(
                new AddTodayJob(
                    [FirstChild.Id],
                    "A job",
                    "",
                    1,
                    Today.AddDays(-1),
                    "bedtime",
                    null),
                CancellationToken.None));

        Assert.Contains("ScheduledDate", exception.Errors.Keys);
        Assert.Contains("AgendaPeriod", exception.Errors.Keys);
        Assert.Empty(repository.Jobs);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task Adult_can_edit_a_pending_job_without_awarding_points()
    {
        var repository = new RecordingRepository([Adult, FirstChild]);
        var job = new Job(Guid.NewGuid(), FirstChild.Id, "Original", "", 2, Today);
        job.MarkComplete(new FixedClock().UtcNow);
        repository.Jobs.Add(job);
        var service = CreateService(repository);

        var updated = await service.UpdateJobAsync(
            job.Id,
            new UpdateTodayJob(
                Adult.Id,
                "Updated",
                "Changed details.",
                6,
                Today.AddDays(1),
                "evening",
                new TimeOnly(19, 0)),
            CancellationToken.None);

        Assert.Equal("Updated", updated.Name);
        Assert.Equal("pendingApproval", updated.Status);
        Assert.Equal(6, updated.Points);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Adult_can_cancel_a_recurring_occurrence_without_changing_its_series()
    {
        var repository = new RecordingRepository([Adult, FirstChild]);
        var series = RecurringJobSeries.Daily(
            Guid.NewGuid(), FirstChild.Id, Adult.Id, "Daily", "", 1,
            AgendaPeriod.Morning, null, Today, Today.AddDays(3), Guid.NewGuid());
        repository.Series.Add(series);
        var job = new Job(
            Guid.NewGuid(), FirstChild.Id, "Daily", "", 1, Today,
            AgendaPeriod.Morning, null, series.Id, RecurrenceFrequency.Daily);
        repository.Jobs.Add(job);
        var service = CreateService(repository);

        var cancelled = await service.CancelJobAsync(
            job.Id, Adult.Id, "Not today.", CancellationToken.None);

        Assert.Equal("cancelled", cancelled.Status);
        Assert.Single(repository.Series);
        Assert.Equal(series.Id, repository.Series[0].Id);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Child_cannot_edit_or_cancel_a_job()
    {
        var repository = new RecordingRepository([Adult, FirstChild]);
        var job = new Job(Guid.NewGuid(), FirstChild.Id, "Original", "", 2, Today);
        repository.Jobs.Add(job);
        var service = CreateService(repository);

        await Assert.ThrowsAsync<JobManagementForbiddenException>(() =>
            service.UpdateJobAsync(
                job.Id,
                new UpdateTodayJob(
                    FirstChild.Id, "Updated", "", 2, Today, "morning", null),
                CancellationToken.None));
        await Assert.ThrowsAsync<JobManagementForbiddenException>(() =>
            service.CancelJobAsync(job.Id, FirstChild.Id, null, CancellationToken.None));

        Assert.Equal(JobStatus.Open, job.Status);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task Requested_date_returns_only_that_days_visible_jobs()
    {
        var requestedDate = Today.AddDays(3);
        var repository = new RecordingRepository([Adult, FirstChild, SecondChild]);
        repository.Jobs.AddRange(
        [
            new Job(Guid.NewGuid(), FirstChild.Id, "Requested", "", 1, requestedDate),
            new Job(Guid.NewGuid(), SecondChild.Id, "Other child", "", 1, requestedDate),
            new Job(Guid.NewGuid(), FirstChild.Id, "Today", "", 1, Today),
        ]);
        var service = CreateService(repository);

        var board = await service.GetAsync(
            FirstChild.Id,
            requestedDate,
            CancellationToken.None);

        Assert.Equal(requestedDate, board.Date);
        Assert.Equal(Today, board.CurrentDate);
        var job = Assert.Single(board.Jobs);
        Assert.Equal("Requested", job.Name);
    }

    [Theory]
    [InlineData(3)] // three years in the future
    [InlineData(-3)] // three years in the past
    public async Task A_date_far_outside_the_browsing_horizon_is_rejected(int years)
    {
        var repository = new RecordingRepository([Adult, FirstChild]);
        var service = CreateService(repository);

        var exception = await Assert.ThrowsAsync<InvalidTodayBoardFilterException>(() =>
            service.GetAsync(Adult.Id, Today.AddYears(years), CancellationToken.None));

        Assert.Contains("Date", exception.Errors.Keys);
        Assert.Null(repository.LastGenerationHorizon);
    }

    [Fact]
    public async Task A_date_at_the_edge_of_the_browsing_horizon_is_accepted()
    {
        var repository = new RecordingRepository([Adult, FirstChild]);
        var service = CreateService(repository);

        var future = await service.GetAsync(Adult.Id, Today.AddYears(2), CancellationToken.None);
        var past = await service.GetAsync(Adult.Id, Today.AddYears(-2), CancellationToken.None);

        Assert.Equal(Today.AddYears(2), future.Date);
        Assert.Equal(Today.AddYears(-2), past.Date);
    }

    [Fact]
    public async Task Adult_filter_returns_one_child_but_keeps_household_pending_count()
    {
        var repository = new RecordingRepository([Adult, FirstChild, SecondChild]);
        var pendingJob = new Job(Guid.NewGuid(), FirstChild.Id, "Pending", "", 1, Today);
        pendingJob.MarkComplete(new DateTimeOffset(2026, 9, 7, 7, 30, 0, TimeSpan.Zero));
        repository.Jobs.AddRange(
        [
            pendingJob,
            new Job(Guid.NewGuid(), SecondChild.Id, "Selected", "", 2, Today),
        ]);
        var service = CreateService(repository);

        var board = await service.GetAsync(
            Adult.Id,
            Today,
            SecondChild.Id,
            CancellationToken.None);

        Assert.Equal(SecondChild.Id, board.SelectedChildId);
        var job = Assert.Single(board.Jobs);
        Assert.Equal(SecondChild.Id, job.ChildId);
        Assert.Equal("Selected", job.Name);
        Assert.Equal(1, board.PendingApprovalCount);
    }

    [Fact]
    public async Task Adult_filter_rejects_a_member_who_is_not_an_active_child()
    {
        var repository = new RecordingRepository([Adult, FirstChild, SecondChild]);
        var service = CreateService(repository);

        var exception = await Assert.ThrowsAsync<InvalidTodayBoardFilterException>(() =>
            service.GetAsync(
                Adult.Id,
                Today,
                Adult.Id,
                CancellationToken.None));

        Assert.Contains("ChildId", exception.Errors.Keys);
        Assert.Null(repository.LastGenerationHorizon);
    }

    [Fact]
    public async Task Child_cannot_use_the_adult_daily_board_filter()
    {
        var repository = new RecordingRepository([Adult, FirstChild, SecondChild]);
        var service = CreateService(repository);

        await Assert.ThrowsAsync<TodayBoardFilterForbiddenException>(() =>
            service.GetAsync(
                FirstChild.Id,
                Today,
                FirstChild.Id,
                CancellationToken.None));

        Assert.Null(repository.LastGenerationHorizon);
    }

    [Fact]
    public async Task Browsing_beyond_the_rolling_horizon_advances_recurrence_generation()
    {
        var requestedDate = Today.AddDays(70);
        var repository = new RecordingRepository([Adult, FirstChild]);
        var service = CreateService(repository);

        await service.GetAsync(FirstChild.Id, requestedDate, CancellationToken.None);

        Assert.Equal(requestedDate, repository.LastGenerationHorizon);
    }

    [Fact]
    public async Task Recurring_assignment_for_two_children_is_idempotent()
    {
        var repository = new RecordingRepository([Adult, FirstChild, SecondChild]);
        var service = CreateService(repository);
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

    private static TodayBoardService CreateService(
        ITodayBoardRepository repository,
        IHouseholdClock? clock = null)
    {
        var householdClock = clock ?? new FixedClock();
        return new TodayBoardService(
            repository,
            householdClock,
            new TurnRotationService(new EmptyTurnRotationRepository(), householdClock));
    }

    [Fact]
    public async Task Take_turns_creates_one_series_that_alternates_between_children()
    {
        var repository = new RecordingRepository([Adult, FirstChild, SecondChild]);
        var service = CreateService(repository);
        var request = TakeTurnsRequest(Guid.NewGuid(), [SecondChild.Id, FirstChild.Id]);

        var created = await service.CreateDailyRecurringJobAsync(request, CancellationToken.None);
        var retried = await service.CreateDailyRecurringJobAsync(request, CancellationToken.None);

        var assignment = Assert.Single(created.Assignments);
        Assert.Equal(SecondChild.Id, assignment.ChildId);
        Assert.Equal([SecondChild.Id, FirstChild.Id], assignment.RotationChildIds);
        Assert.Equal(4, assignment.OccurrenceCount);
        Assert.Single(repository.Series);
        Assert.Equal(
            [SecondChild.Id, FirstChild.Id, SecondChild.Id, FirstChild.Id],
            repository.Jobs.OrderBy(job => job.ScheduledDate).Select(job => job.ChildId));
        Assert.False(retried.WasCreated);
        Assert.Equal(assignment.SeriesId, Assert.Single(retried.Assignments).SeriesId);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Take_turns_retry_with_a_different_rotation_conflicts()
    {
        var repository = new RecordingRepository([Adult, FirstChild, SecondChild]);
        var service = CreateService(repository);
        var requestId = Guid.NewGuid();
        await service.CreateDailyRecurringJobAsync(
            TakeTurnsRequest(requestId, [FirstChild.Id, SecondChild.Id]),
            CancellationToken.None);

        await Assert.ThrowsAsync<DailyRecurringJobRequestConflictException>(() =>
            service.CreateDailyRecurringJobAsync(
                TakeTurnsRequest(requestId, [SecondChild.Id, FirstChild.Id]),
                CancellationToken.None));
        await Assert.ThrowsAsync<DailyRecurringJobRequestConflictException>(() =>
            service.CreateDailyRecurringJobAsync(
                TakeTurnsRequest(requestId, [FirstChild.Id, SecondChild.Id]) with
                {
                    AssignmentMode = "eachChild",
                },
                CancellationToken.None));
    }

    [Fact]
    public async Task Take_turns_needs_two_children_and_a_known_mode()
    {
        var repository = new RecordingRepository([Adult, FirstChild, SecondChild]);
        var service = CreateService(repository);

        var tooFew = await Assert.ThrowsAsync<InvalidDailyRecurringJobException>(() =>
            service.CreateDailyRecurringJobAsync(
                TakeTurnsRequest(Guid.NewGuid(), [FirstChild.Id]),
                CancellationToken.None));
        var unknownMode = await Assert.ThrowsAsync<InvalidDailyRecurringJobException>(() =>
            service.CreateDailyRecurringJobAsync(
                TakeTurnsRequest(Guid.NewGuid(), [FirstChild.Id, SecondChild.Id]) with
                {
                    AssignmentMode = "sometimes",
                },
                CancellationToken.None));

        Assert.Contains(nameof(CreateDailyRecurringJob.ChildIds), tooFew.Errors.Keys);
        Assert.Contains(nameof(CreateDailyRecurringJob.AssignmentMode), unknownMode.Errors.Keys);
        Assert.Empty(repository.Series);
        Assert.Equal(0, repository.SaveCount);
    }

    private static CreateDailyRecurringJob TakeTurnsRequest(Guid requestId, IReadOnlyList<Guid> childIds)
    {
        return new CreateDailyRecurringJob(
            requestId,
            Adult.Id,
            childIds,
            "Tidy the table",
            "After dinner.",
            1,
            "evening",
            null,
            Today,
            Today.AddDays(3),
            "takeTurns");
    }

    private sealed class FixedClock : IHouseholdClock
    {
        public DateOnly Today => TodayBoardServiceTests.Today;

        public DateTimeOffset UtcNow => new(2026, 9, 7, 8, 0, 0, TimeSpan.Zero);
    }

    private sealed class EmptyTurnRotationRepository : ITurnRotationRepository
    {
        public Task<IReadOnlyList<TurnRotationRevision>> GetRevisionsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TurnRotationRevision>>([]);

        public Task<HouseholdMember?> GetMemberAsync(Guid memberId, CancellationToken cancellationToken) =>
            Task.FromResult<HouseholdMember?>(null);

        public Task<IReadOnlyList<HouseholdMember>> GetMembersAsync(
            IReadOnlyCollection<Guid> memberIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HouseholdMember>>([]);

        public Task<IReadOnlyList<HouseholdMember>> GetActiveChildrenAsync(
            IReadOnlyCollection<Guid> childIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HouseholdMember>>([]);

        public Task AddRevisionAsync(
            TurnRotationRevision revision,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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

        public DateOnly? LastGenerationHorizon { get; private set; }

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

        public Task<IReadOnlyList<Job>> GetJobsAsync(IReadOnlyCollection<Guid> childIds, DateOnly scheduledDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Job>>(Jobs
                .Where(job => childIds.Contains(job.ChildId) && job.ScheduledDate == scheduledDate)
                .ToArray());
        public Task<Job?> GetJobAsync(Guid jobId, CancellationToken cancellationToken) =>
            Task.FromResult(Jobs.SingleOrDefault(job => job.Id == jobId));
        public Task<IReadOnlyList<TodayJobRejection>> GetLatestRejectionsAsync(IReadOnlyCollection<Guid> childIds, DateOnly scheduledDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TodayJobRejection>>([]);
        public Task<IReadOnlyList<Job>> GetJobsInRangeAsync(IReadOnlyCollection<Guid> childIds, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Job>>(Jobs
                .Where(job => childIds.Contains(job.ChildId) && job.ScheduledDate >= startDate && job.ScheduledDate <= endDate)
                .OrderBy(job => job.ScheduledDate)
                .ToArray());
        public Task<IReadOnlyList<TodayJobRejection>> GetLatestRejectionsInRangeAsync(IReadOnlyCollection<Guid> childIds, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TodayJobRejection>>([]);
        public Task<IReadOnlyList<RecurringJobSeries>> GetRecurringJobSeriesNeedingGenerationAsync(DateOnly horizon, CancellationToken cancellationToken)
        {
            LastGenerationHorizon = horizon;
            return Task.FromResult<IReadOnlyList<RecurringJobSeries>>([]);
        }
        public Task<TodayPointsSummary> GetPointsSummaryAsync(Guid childId, CancellationToken cancellationToken) =>
            Task.FromResult(new TodayPointsSummary(0, []));
        public Task AddPointsAwardAsync(PointsLedgerEntry entry, CancellationToken cancellationToken) => throw Unused();
        public Task AddReviewDecisionAsync(JobReviewDecision decision, CancellationToken cancellationToken) => throw Unused();

        private static NotSupportedException Unused() => new("This operation is not used by the test.");
    }
}
