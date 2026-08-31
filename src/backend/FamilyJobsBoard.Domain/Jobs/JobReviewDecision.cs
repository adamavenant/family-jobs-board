namespace FamilyJobsBoard.Domain.Jobs;

public sealed class JobReviewDecision
{
    public const int MaximumReasonLength = 500;

    private JobReviewDecision()
    {
    }

    public JobReviewDecision(
        Guid id,
        Guid jobId,
        JobReviewOutcome outcome,
        string? reason,
        DateTimeOffset decidedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A review decision needs an ID.", nameof(id));
        }

        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("A review decision needs a job.", nameof(jobId));
        }

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), "The review outcome is invalid.");
        }

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason?.Length > MaximumReasonLength)
        {
            throw new ArgumentException(
                $"A rejection reason cannot exceed {MaximumReasonLength} characters.",
                nameof(reason));
        }

        Id = id;
        JobId = jobId;
        Outcome = outcome;
        Reason = trimmedReason;
        DecidedAtUtc = decidedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }

    public Guid JobId { get; private set; }

    public JobReviewOutcome Outcome { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset DecidedAtUtc { get; private set; }
}
