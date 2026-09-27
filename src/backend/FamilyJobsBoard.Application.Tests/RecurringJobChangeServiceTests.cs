using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Application.Today;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using Xunit;

namespace FamilyJobsBoard.Application.Tests;

public sealed class RecurringJobChangeServiceTests
{
    private static readonly DateOnly Today = new(2026, 9, 7);
    private static readonly HouseholdMember Adult = new(
        Guid.NewGuid(), "Addie", "Avenant", HouseholdRole.Adult);
    private static readonly HouseholdMember Child = new(
        Guid.NewGuid(), "Fredster", "Avenant", HouseholdRole.Child);

    [Fact]
    public async Task All_future_weekday_edit_reconciles_materialized_weekends()
    {
        var (repository, series, jobs) = DailySeries(Today, Today.AddDays(6));
        var service = new RecurringJobChangeService(repository, new FixedClock());
        var requestId = Guid.NewGuid();

        var result = await service.ApplyAsync(
            jobs[0].Id,
            Adult.Id,
            new ApplyRecurringJobChange(
                requestId,
                Edit("allFuture", series.Version, Today, 3, "weekly",
                    ["monday", "tuesday", "wednesday", "thursday", "friday"])),
            CancellationToken.None);

        Assert.Equal(5, result.Impact.UpdatedCount);
        Assert.Equal(2, result.Impact.CancelledCount);
        Assert.Equal(RecurrenceFrequency.Weekly, series.Frequency);
        Assert.Equal(
            new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday },
            series.SelectedWeekdays());
        Assert.All(
            repository.Jobs.Where(job => job.ScheduledDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday),
            job => Assert.Equal(JobStatus.Cancelled, job.Status));
        Assert.Single(repository.Revisions);
        Assert.Single(repository.Changes);
    }

    [Fact]
    public async Task Approved_and_cancelled_occurrences_are_never_changed_or_recreated()
    {
        var (repository, series, jobs) = DailySeries(Today, Today.AddDays(2));
        jobs[0].MarkComplete(new FixedClock().UtcNow);
        jobs[0].Approve(new FixedClock().UtcNow.AddHours(1));
        jobs[1].Cancel(Adult.Id, new FixedClock().UtcNow, null);
        var service = new RecurringJobChangeService(repository, new FixedClock());

        var result = await service.ApplyAsync(
            jobs[2].Id,
            Adult.Id,
            new ApplyRecurringJobChange(
                Guid.NewGuid(),
                Edit("all", series.Version, jobs[2].ScheduledDate, 7, "weekly", ["wednesday"])),
            CancellationToken.None);

        Assert.Equal(1, result.Impact.ApprovedSkippedCount);
        Assert.Equal(1, result.Impact.CancelledSkippedCount);
        Assert.Equal(JobStatus.Approved, jobs[0].Status);
        Assert.Equal(3, jobs[0].Points);
        Assert.Equal(JobStatus.Cancelled, jobs[1].Status);
        Assert.Equal(3, repository.Jobs.Count);
    }

    [Fact]
    public async Task Point_increase_is_not_applied_to_past_occurrences()
    {
        var start = Today.AddDays(-2);
        var (repository, series, jobs) = DailySeries(start, Today);
        var service = new RecurringJobChangeService(repository, new FixedClock());

        var preview = await service.PreviewAsync(
            jobs[0].Id,
            Adult.Id,
            Edit("all", series.Version, jobs[0].ScheduledDate, 9, "daily", []),
            CancellationToken.None);
        var result = await service.ApplyAsync(
            jobs[0].Id,
            Adult.Id,
            new ApplyRecurringJobChange(
                Guid.NewGuid(),
                Edit("all", series.Version, jobs[0].ScheduledDate, 9, "daily", [])),
            CancellationToken.None);

        Assert.Equal(2, preview.RetrospectivePointIncreaseSkippedCount);
        Assert.Equal(2, result.Impact.RetrospectivePointIncreaseSkippedCount);
        Assert.Equal(3, jobs[0].Points);
        Assert.Equal(3, jobs[1].Points);
        Assert.Equal(9, jobs[2].Points);
    }

    [Fact]
    public async Task Identical_request_retry_returns_stored_result_without_more_writes()
    {
        var (repository, series, jobs) = DailySeries(Today, Today.AddDays(1));
        var service = new RecurringJobChangeService(repository, new FixedClock());
        var request = new ApplyRecurringJobChange(
            Guid.NewGuid(),
            Edit("allFuture", series.Version, Today, 4, "daily", []));

        var first = await service.ApplyAsync(jobs[0].Id, Adult.Id, request, CancellationToken.None);
        var retry = await service.ApplyAsync(jobs[0].Id, Adult.Id, request, CancellationToken.None);

        Assert.Equal(first.Impact.UpdatedCount, retry.Impact.UpdatedCount);
        Assert.Equal(1, repository.SaveCount);
        Assert.Single(repository.Changes);
    }

    [Fact]
    public async Task All_cancellation_ends_series_but_preserves_approved_job()
    {
        var (repository, series, jobs) = DailySeries(Today, Today.AddDays(2));
        jobs[0].MarkComplete(new FixedClock().UtcNow);
        jobs[0].Approve(new FixedClock().UtcNow);
        var service = new RecurringJobChangeService(repository, new FixedClock());

        var result = await service.ApplyAsync(
            jobs[1].Id,
            Adult.Id,
            new ApplyRecurringJobChange(
                Guid.NewGuid(),
                new RecurringJobChangeInput(
                    "cancel", "all", series.Version, null, null, 0, null, null,
                    null, null, null, null, null)),
            CancellationToken.None);

        Assert.Equal(2, result.Impact.CancelledCount);
        Assert.Equal(1, result.Impact.ApprovedSkippedCount);
        Assert.Equal(JobStatus.Approved, jobs[0].Status);
        Assert.Equal(series.StartDate.AddDays(-1), series.EndDate);
    }

    [Fact]
    public async Task Take_turns_reconciliation_continues_from_last_retained_turn()
    {
        var secondChild = new HouseholdMember(
            Guid.NewGuid(), "Harrie", "Avenant", HouseholdRole.Child);
        var series = RecurringJobSeries.Weekly(
            Guid.NewGuid(), Child.Id, Adult.Id, "Empty school bags", "", 3,
            AgendaPeriod.Evening, null, Today, null,
            [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday],
            Guid.NewGuid(), [Child.Id, secondChild.Id]);
        var jobs = series.GenerateOccurrencesThrough(Today.AddDays(4))
            .Select(occurrence => new Job(
                Guid.NewGuid(), occurrence.ChildId, series.Name, series.Description, series.Points,
                occurrence.Date, series.AgendaPeriod, series.ScheduledTime, series.Id, series.Frequency))
            .ToArray();
        var repository = new RecordingRepository([Adult, Child, secondChild], series, jobs);
        var service = new RecurringJobChangeService(repository, new FixedClock());

        await service.ApplyAsync(
            jobs[0].Id,
            Adult.Id,
            new ApplyRecurringJobChange(
                Guid.NewGuid(),
                Edit("allFuture", series.Version, Today, 3, "daily", [])),
            CancellationToken.None);

        var active = repository.Jobs.Where(job => job.Status != JobStatus.Cancelled)
            .OrderBy(job => job.ScheduledDate)
            .ToArray();
        Assert.Equal(5, active.Length);
        Assert.Equal(
            [Child.Id, secondChild.Id, Child.Id, secondChild.Id, Child.Id],
            active.Select(job => job.ChildId).ToArray());
        Assert.Equal(1, series.NextTurnIndex);
    }

    [Fact]
    public async Task This_only_ignores_incomplete_series_pattern_fields()
    {
        var (repository, series, jobs) = DailySeries(Today, Today);
        var service = new RecurringJobChangeService(repository, new FixedClock());
        var input = Edit("thisOnly", series.Version, Today, 5, "weekly", []);

        var result = await service.ApplyAsync(
            jobs[0].Id,
            Adult.Id,
            new ApplyRecurringJobChange(Guid.NewGuid(), input),
            CancellationToken.None);

        Assert.Equal(1, result.Impact.UpdatedCount);
        Assert.Equal(5, jobs[0].Points);
        Assert.Equal(RecurrenceFrequency.Daily, series.Frequency);
    }

    [Fact]
    public async Task This_only_cancellation_changes_only_the_anchor()
    {
        var (repository, series, jobs) = DailySeries(Today, Today.AddDays(1));
        var service = new RecurringJobChangeService(repository, new FixedClock());
        var input = new RecurringJobChangeInput(
            "cancel", "thisOnly", series.Version, null, null, 0, null, null,
            null, null, null, null, null);

        var result = await service.ApplyAsync(
            jobs[0].Id,
            Adult.Id,
            new ApplyRecurringJobChange(Guid.NewGuid(), input),
            CancellationToken.None);

        Assert.Equal(1, result.Impact.CancelledCount);
        Assert.Equal(JobStatus.Cancelled, jobs[0].Status);
        Assert.Equal(JobStatus.Open, jobs[1].Status);
        Assert.Null(series.EndDate);
    }

    [Fact]
    public async Task This_only_date_collision_is_rejected_before_any_write()
    {
        var (repository, series, jobs) = DailySeries(Today, Today.AddDays(1));
        var service = new RecurringJobChangeService(repository, new FixedClock());
        var input = Edit("thisOnly", series.Version, Today.AddDays(1), 3, "daily", []);

        await Assert.ThrowsAsync<RecurringJobChangeConflictException>(() => service.ApplyAsync(
            jobs[0].Id,
            Adult.Id,
            new ApplyRecurringJobChange(Guid.NewGuid(), input),
            CancellationToken.None));

        Assert.Equal(Today, jobs[0].ScheduledDate);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task Stale_series_version_is_rejected_before_any_write()
    {
        var (repository, series, jobs) = DailySeries(Today, Today);
        var service = new RecurringJobChangeService(repository, new FixedClock());
        var staleVersion = series.Version;
        series.GenerateOccurrencesThrough(Today.AddDays(1));

        await Assert.ThrowsAsync<RecurringJobChangeConflictException>(() => service.ApplyAsync(
            jobs[0].Id,
            Adult.Id,
            new ApplyRecurringJobChange(
                Guid.NewGuid(),
                Edit("allFuture", staleVersion, Today, 4, "daily", [])),
            CancellationToken.None));

        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task All_can_change_daily_to_monthly_with_short_month_fallback()
    {
        var end = new DateOnly(2026, 10, 31);
        var (repository, series, jobs) = DailySeries(Today, end);
        var service = new RecurringJobChangeService(repository, new FixedClock());
        var input = Edit("all", series.Version, Today, 3, "monthly", []) with
        {
            DayOfMonth = 31,
            EndDate = end,
        };

        await service.ApplyAsync(
            jobs[0].Id,
            Adult.Id,
            new ApplyRecurringJobChange(Guid.NewGuid(), input),
            CancellationToken.None);

        Assert.Equal(RecurrenceFrequency.Monthly, series.Frequency);
        Assert.Equal(31, series.MonthlyDay);
        Assert.Equal(
            [new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 31)],
            repository.Jobs
                .Where(job => job.Status == JobStatus.Open)
                .Select(job => job.ScheduledDate)
                .Order()
                .ToArray());
    }

    private static RecurringJobChangeInput Edit(
        string scope,
        int version,
        DateOnly scheduledDate,
        int points,
        string frequency,
        IReadOnlyList<string> weekdays) => new(
            "edit",
            scope,
            version,
            "Empty school bags",
            "Put everything away.",
            points,
            scheduledDate,
            "evening",
            new TimeOnly(18, 0),
            frequency,
            weekdays,
            null,
            null);

    private static (RecordingRepository Repository, RecurringJobSeries Series, Job[] Jobs)
        DailySeries(DateOnly start, DateOnly end)
    {
        var series = RecurringJobSeries.Daily(
            Guid.NewGuid(), Child.Id, Adult.Id, "Empty school bags", "", 3,
            AgendaPeriod.Evening, null, start, null, Guid.NewGuid());
        var occurrences = series.GenerateOccurrencesThrough(end);
        var jobs = occurrences.Select(occurrence => new Job(
            Guid.NewGuid(), occurrence.ChildId, series.Name, series.Description, series.Points,
            occurrence.Date, series.AgendaPeriod, series.ScheduledTime, series.Id, series.Frequency))
            .ToArray();
        var repository = new RecordingRepository([Adult, Child], series, jobs);
        return (repository, series, jobs);
    }

    private sealed class FixedClock : IHouseholdClock
    {
        public DateOnly Today => RecurringJobChangeServiceTests.Today;
        public DateTimeOffset UtcNow => new(2026, 9, 7, 8, 0, 0, TimeSpan.Zero);
    }

    private sealed class RecordingRepository : IRecurringJobChangeRepository
    {
        private readonly IReadOnlyList<HouseholdMember> _members;

        public RecordingRepository(
            IReadOnlyList<HouseholdMember> members,
            RecurringJobSeries series,
            IReadOnlyList<Job> jobs)
        {
            _members = members;
            Series = series;
            Jobs.AddRange(jobs);
        }

        public RecurringJobSeries Series { get; }
        public List<Job> Jobs { get; } = [];
        public List<RecurringJobChange> Changes { get; } = [];
        public List<RecurringJobSeriesRevision> Revisions { get; } = [];
        public int SaveCount { get; private set; }

        public Task<HouseholdMember?> GetMemberAsync(Guid memberId, CancellationToken cancellationToken) =>
            Task.FromResult(_members.SingleOrDefault(member => member.Id == memberId));

        public Task<Job?> GetJobAsync(Guid jobId, bool tracking, CancellationToken cancellationToken) =>
            Task.FromResult(Jobs.SingleOrDefault(job => job.Id == jobId));

        public Task<RecurringJobSeries?> GetSeriesAsync(
            Guid seriesId,
            bool tracking,
            CancellationToken cancellationToken) =>
            Task.FromResult<RecurringJobSeries?>(Series.Id == seriesId ? Series : null);

        public Task<IReadOnlyList<Job>> GetSeriesJobsAsync(
            Guid seriesId,
            bool tracking,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Job>>(Jobs.Where(job => job.RecurringJobSeriesId == seriesId).ToArray());

        public Task<RecurringJobChange?> GetChangeAsync(Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult(Changes.SingleOrDefault(change => change.Id == requestId));

        public Task AddJobsAsync(IReadOnlyCollection<Job> jobs, CancellationToken cancellationToken)
        {
            Jobs.AddRange(jobs);
            return Task.CompletedTask;
        }

        public Task AddChangeAsync(RecurringJobChange change, CancellationToken cancellationToken)
        {
            Changes.Add(change);
            return Task.CompletedTask;
        }

        public Task AddRevisionAsync(
            RecurringJobSeriesRevision revision,
            CancellationToken cancellationToken)
        {
            Revisions.Add(revision);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
