using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Domain.Points;
using Xunit;

namespace FamilyJobsBoard.Domain.Tests;

public sealed class JobApprovalReversalTests
{
    private static readonly DateTimeOffset CompletedAt = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ApprovedAt = CompletedAt.AddHours(1);

    [Fact]
    public void Reversing_a_child_submitted_approval_returns_it_for_approval_with_its_completion()
    {
        var job = NewJob();
        job.MarkComplete(CompletedAt);
        job.Approve(ApprovedAt);

        job.ReverseApproval();

        Assert.Equal(JobStatus.PendingApproval, job.Status);
        Assert.Equal(CompletedAt, job.CompletedAtUtc);
        Assert.Null(job.ApprovedAtUtc);
        Assert.False(job.CompletedByAdult);
        Assert.Equal(1, job.ApprovalCount);
    }

    [Fact]
    public void Reversing_an_adult_completion_reopens_the_job_without_a_completion()
    {
        var job = NewJob();
        job.CompleteForChild(CompletedAt);
        Assert.Equal(JobStatus.Approved, job.Status);
        Assert.Equal(CompletedAt, job.CompletedAtUtc);
        Assert.Equal(CompletedAt, job.ApprovedAtUtc);
        Assert.True(job.CompletedByAdult);

        job.ReverseApproval();

        Assert.Equal(JobStatus.Open, job.Status);
        Assert.Null(job.CompletedAtUtc);
        Assert.Null(job.ApprovedAtUtc);
        Assert.False(job.CompletedByAdult);
        Assert.Equal(1, job.ApprovalCount);
    }

    [Fact]
    public void A_reversed_submission_can_be_approved_again_rejected_or_cancelled()
    {
        var approvedAgain = ReversedSubmission();
        approvedAgain.Approve(ApprovedAt.AddHours(1));
        Assert.Equal(JobStatus.Approved, approvedAgain.Status);
        Assert.Equal(2, approvedAgain.ApprovalCount);

        var rejected = ReversedSubmission();
        rejected.Reject();
        Assert.Equal(JobStatus.Open, rejected.Status);
        Assert.Null(rejected.CompletedAtUtc);

        var cancelled = ReversedSubmission();
        cancelled.Cancel(Guid.NewGuid(), ApprovedAt.AddHours(1), null);
        Assert.Equal(JobStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public void A_reopened_adult_completion_can_be_completed_again_edited_or_cancelled()
    {
        var completedAgain = ReopenedAdultCompletion();
        completedAgain.MarkComplete(CompletedAt.AddHours(2));
        Assert.Equal(JobStatus.PendingApproval, completedAgain.Status);
        Assert.False(completedAgain.CompletedByAdult);

        var markedDoneAgain = ReopenedAdultCompletion();
        markedDoneAgain.CompleteForChild(CompletedAt.AddHours(2));
        Assert.Equal(JobStatus.Approved, markedDoneAgain.Status);
        Assert.Equal(2, markedDoneAgain.ApprovalCount);

        var edited = ReopenedAdultCompletion();
        edited.Edit("Feed the cat", "", 7, edited.ScheduledDate, AgendaPeriod.Evening, null);
        Assert.Equal(7, edited.Points);

        var cancelled = ReopenedAdultCompletion();
        cancelled.Cancel(Guid.NewGuid(), CompletedAt.AddHours(2), "Not needed.");
        Assert.Equal(JobStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public void Only_an_approved_job_can_be_reversed()
    {
        var open = NewJob();
        var pending = NewJob();
        pending.MarkComplete(CompletedAt);
        var cancelled = NewJob();
        cancelled.Cancel(Guid.NewGuid(), CompletedAt, null);
        var reversed = ReversedSubmission();

        foreach (var job in new[] { open, pending, cancelled, reversed })
        {
            var status = job.Status;
            var exception = Assert.Throws<JobApprovalReversalRejectedException>(job.ReverseApproval);
            Assert.Equal(job.Id, exception.JobId);
            Assert.Equal(status, job.Status);
        }
    }

    [Fact]
    public void Adult_completion_needs_an_open_job()
    {
        var job = NewJob();
        job.MarkComplete(CompletedAt);

        Assert.Throws<JobCompletionRejectedException>(() => job.CompleteForChild(ApprovedAt));
        Assert.Equal(JobStatus.PendingApproval, job.Status);
        Assert.False(job.CompletedByAdult);
        Assert.Equal(0, job.ApprovalCount);
    }

    [Fact]
    public void A_job_award_snapshots_the_approval_it_belongs_to()
    {
        var job = ApprovedJob();
        job.ReverseApproval();
        job.Approve(ApprovedAt.AddHours(1));

        var award = PointsLedgerEntry.ForJobApproval(Guid.NewGuid(), job, ApprovedAt.AddHours(1));
        job.ReverseApproval();
        job.Edit("Walk the dog", "", 9, job.ScheduledDate, AgendaPeriod.Evening, null);

        Assert.Equal(job.Id, award.JobId);
        Assert.Equal(job.ChildId, award.ChildId);
        Assert.Equal("Feed the dog", award.JobName);
        Assert.Equal(2, award.JobApprovalNumber);
        Assert.Equal(5, award.Amount);
    }

    [Fact]
    public void Approval_reversal_takes_back_the_award_as_a_new_entry()
    {
        var job = ApprovedJob();
        var award = PointsLedgerEntry.ForJobApproval(Guid.NewGuid(), job, ApprovedAt);
        var reversedAt = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.FromHours(2));

        var reversal = PointsLedgerEntry.ForApprovalReversal(Guid.NewGuid(), award, reversedAt);

        Assert.Equal(job.ChildId, reversal.ChildId);
        Assert.Equal(job.Id, reversal.JobId);
        Assert.Equal("Feed the dog", reversal.JobName);
        Assert.Equal(award.Id, reversal.ReversedEntryId);
        Assert.Equal(-5, reversal.Amount);
        Assert.Null(reversal.JobApprovalNumber);
        Assert.Equal(reversedAt.ToUniversalTime(), reversal.AwardedAtUtc);
        Assert.True(reversal.IsApprovalReversal);
        Assert.False(award.IsApprovalReversal);
        Assert.Equal(5, award.Amount);
        Assert.Equal(1, award.JobApprovalNumber);
    }

    [Fact]
    public void A_zero_point_award_is_taken_back_with_a_zero_entry()
    {
        var job = new Job(Guid.NewGuid(), Guid.NewGuid(), "Smile", "", 0, new DateOnly(2026, 10, 14));
        job.CompleteForChild(CompletedAt);
        var award = PointsLedgerEntry.ForJobApproval(Guid.NewGuid(), job, CompletedAt);

        var reversal = PointsLedgerEntry.ForApprovalReversal(Guid.NewGuid(), award, ApprovedAt);

        Assert.Equal(0, reversal.Amount);
        Assert.Equal(award.Id, reversal.ReversedEntryId);
    }

    [Fact]
    public void Only_a_job_award_can_be_reversed()
    {
        var job = ApprovedJob();
        var child = job.ChildId;
        var award = PointsLedgerEntry.ForJobApproval(Guid.NewGuid(), job, ApprovedAt);
        var reversal = PointsLedgerEntry.ForApprovalReversal(Guid.NewGuid(), award, ApprovedAt);
        var behaviour = PointsLedgerEntry.ForGoodBehaviour(Guid.NewGuid(), child, Guid.NewGuid(), 5, ApprovedAt);
        var redemption = PointsLedgerEntry.ForRedemption(Guid.NewGuid(), child, Guid.NewGuid(), 5, ApprovedAt);

        foreach (var entry in new[] { reversal, behaviour, redemption })
        {
            Assert.Throws<ArgumentException>(() =>
                PointsLedgerEntry.ForApprovalReversal(Guid.NewGuid(), entry, ApprovedAt));
        }
    }

    [Fact]
    public void Only_an_approved_job_awards_points()
    {
        var pending = NewJob();
        pending.MarkComplete(CompletedAt);

        foreach (var job in new[] { NewJob(), pending, ReversedSubmission() })
        {
            Assert.Throws<ArgumentException>(() =>
                PointsLedgerEntry.ForJobApproval(Guid.NewGuid(), job, ApprovedAt));
        }
    }

    private static Job ApprovedJob()
    {
        var job = NewJob();
        job.MarkComplete(CompletedAt);
        job.Approve(ApprovedAt);
        return job;
    }

    private static Job ReversedSubmission()
    {
        var job = NewJob();
        job.MarkComplete(CompletedAt);
        job.Approve(ApprovedAt);
        job.ReverseApproval();
        return job;
    }

    private static Job ReopenedAdultCompletion()
    {
        var job = NewJob();
        job.CompleteForChild(CompletedAt);
        job.ReverseApproval();
        return job;
    }

    private static Job NewJob() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Feed the dog",
        "Fill the food bowl.",
        5,
        new DateOnly(2026, 10, 14));
}
