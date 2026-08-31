using FamilyJobsBoard.Domain.Jobs;
using Xunit;

namespace FamilyJobsBoard.Domain.Tests;

public sealed class JobReviewTests
{
    [Fact]
    public void Pending_job_can_be_rejected_and_submitted_again()
    {
        var job = NewJob();
        var completedAtUtc = new DateTimeOffset(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);

        Assert.Throws<JobRejectionRejectedException>(() => job.Reject());

        job.MarkComplete(completedAtUtc);
        job.Reject();

        Assert.Equal(JobStatus.Open, job.Status);
        Assert.Null(job.CompletedAtUtc);
        Assert.Null(job.ApprovedAtUtc);
        Assert.Throws<JobRejectionRejectedException>(() => job.Reject());

        job.MarkComplete(completedAtUtc.AddHours(1));

        Assert.Equal(JobStatus.PendingApproval, job.Status);
        Assert.Equal(completedAtUtc.AddHours(1), job.CompletedAtUtc);
    }

    [Fact]
    public void Review_decision_normalises_reason_and_time()
    {
        var localTime = new DateTimeOffset(2026, 8, 31, 13, 0, 0, TimeSpan.FromHours(2));

        var rejected = new JobReviewDecision(
            Guid.NewGuid(),
            Guid.NewGuid(),
            JobReviewOutcome.Rejected,
            "  Please wipe underneath the bowl.  ",
            localTime);
        var approved = new JobReviewDecision(
            Guid.NewGuid(),
            Guid.NewGuid(),
            JobReviewOutcome.Approved,
            "   ",
            localTime);

        Assert.Equal("Please wipe underneath the bowl.", rejected.Reason);
        Assert.Equal(localTime.ToUniversalTime(), rejected.DecidedAtUtc);
        Assert.Null(approved.Reason);
    }

    [Fact]
    public void Review_decision_rejects_an_overlong_reason()
    {
        var action = () => new JobReviewDecision(
            Guid.NewGuid(),
            Guid.NewGuid(),
            JobReviewOutcome.Rejected,
            new string('r', JobReviewDecision.MaximumReasonLength + 1),
            DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(action);
    }

    private static Job NewJob()
    {
        return new Job(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Feed the dog",
            "Fill the food bowl.",
            5,
            new DateOnly(2026, 8, 31));
    }
}
