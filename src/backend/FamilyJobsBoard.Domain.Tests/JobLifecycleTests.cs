using FamilyJobsBoard.Domain.Jobs;
using Xunit;

namespace FamilyJobsBoard.Domain.Tests;

public sealed class JobLifecycleTests
{
    [Fact]
    public void Open_job_can_be_edited_without_changing_its_state()
    {
        var job = CreateJob();

        job.Edit(
            "  Updated job  ",
            "  Updated description.  ",
            8,
            new DateOnly(2026, 9, 24),
            AgendaPeriod.Evening,
            new TimeOnly(18, 30));

        Assert.Equal(JobStatus.Open, job.Status);
        Assert.Equal("Updated job", job.Name);
        Assert.Equal("Updated description.", job.Description);
        Assert.Equal(8, job.Points);
        Assert.Equal(new DateOnly(2026, 9, 24), job.ScheduledDate);
        Assert.Equal(AgendaPeriod.Evening, job.AgendaPeriod);
        Assert.Equal(new TimeOnly(18, 30), job.ScheduledTime);
    }

    [Fact]
    public void Pending_job_can_be_edited_without_clearing_completion()
    {
        var job = CreateJob();
        var completedAt = new DateTimeOffset(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);
        job.MarkComplete(completedAt);

        job.Edit("Updated", "", 4, job.ScheduledDate, AgendaPeriod.Morning, null);

        Assert.Equal(JobStatus.PendingApproval, job.Status);
        Assert.Equal(completedAt, job.CompletedAtUtc);
    }

    [Fact]
    public void Approved_job_cannot_be_edited_or_cancelled()
    {
        var job = CreateJob();
        job.MarkComplete(new DateTimeOffset(2026, 9, 23, 8, 0, 0, TimeSpan.Zero));
        job.Approve(new DateTimeOffset(2026, 9, 23, 9, 0, 0, TimeSpan.Zero));

        Assert.Throws<JobEditRejectedException>(() =>
            job.Edit("Updated", "", 4, job.ScheduledDate, AgendaPeriod.Morning, null));
        Assert.Throws<JobCancellationRejectedException>(() =>
            job.Cancel(Guid.NewGuid(), DateTimeOffset.UtcNow, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Open_or_pending_job_can_be_cancelled(bool completeFirst)
    {
        var job = CreateJob();
        if (completeFirst)
        {
            job.MarkComplete(new DateTimeOffset(2026, 9, 23, 8, 0, 0, TimeSpan.Zero));
        }
        var adultId = Guid.NewGuid();
        var cancelledAt = new DateTimeOffset(2026, 9, 23, 9, 0, 0, TimeSpan.FromHours(2));

        job.Cancel(adultId, cancelledAt, "  No longer needed.  ");

        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Equal(adultId, job.CancelledByMemberId);
        Assert.Equal(cancelledAt.ToUniversalTime(), job.CancelledAtUtc);
        Assert.Equal("No longer needed.", job.CancellationReason);
        Assert.Throws<JobCancellationRejectedException>(() =>
            job.Cancel(adultId, cancelledAt, null));
    }

    private static Job CreateJob() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Original",
        "Original description.",
        3,
        new DateOnly(2026, 9, 23));
}
