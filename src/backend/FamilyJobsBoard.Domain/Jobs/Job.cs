namespace FamilyJobsBoard.Domain.Jobs;

public sealed class Job
{
    public const int MaximumNameLength = 160;
    public const int MaximumDescriptionLength = 1000;
    public const int MaximumCancellationReasonLength = 500;

    private Job()
    {
    }

    public Job(
        Guid id,
        Guid childId,
        string name,
        string description,
        int points,
        DateOnly scheduledDate,
        AgendaPeriod agendaPeriod = AgendaPeriod.Unscheduled,
        TimeOnly? scheduledTime = null,
        Guid? recurringJobSeriesId = null,
        RecurrenceFrequency? recurrenceFrequency = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A job needs an ID.", nameof(id));
        }

        if (childId == Guid.Empty)
        {
            throw new ArgumentException("A job needs an assigned child.", nameof(childId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A job needs a name.", nameof(name));
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > MaximumNameLength)
        {
            throw new ArgumentException(
                $"A job name cannot exceed {MaximumNameLength} characters.",
                nameof(name));
        }

        var trimmedDescription = description.Trim();
        if (trimmedDescription.Length > MaximumDescriptionLength)
        {
            throw new ArgumentException(
                $"A job description cannot exceed {MaximumDescriptionLength} characters.",
                nameof(description));
        }

        if (points < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(points), "Points cannot be negative.");
        }

        if ((recurringJobSeriesId is null) != (recurrenceFrequency is null))
        {
            throw new ArgumentException(
                "Recurring jobs need both a series ID and recurrence frequency.",
                nameof(recurringJobSeriesId));
        }

        Id = id;
        ChildId = childId;
        Name = trimmedName;
        Description = trimmedDescription;
        Points = points;
        ScheduledDate = scheduledDate;
        AgendaPeriod = agendaPeriod;
        ScheduledTime = scheduledTime;
        RecurringJobSeriesId = recurringJobSeriesId;
        RecurrenceFrequency = recurrenceFrequency;
        Status = JobStatus.Open;
    }

    public Guid Id { get; private set; }

    public Guid ChildId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public int Points { get; private set; }

    public DateOnly ScheduledDate { get; private set; }

    public AgendaPeriod AgendaPeriod { get; private set; }

    public TimeOnly? ScheduledTime { get; private set; }

    public Guid? RecurringJobSeriesId { get; private set; }

    public RecurrenceFrequency? RecurrenceFrequency { get; private set; }

    public JobStatus Status { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public DateTimeOffset? ApprovedAtUtc { get; private set; }

    public Guid? CancelledByMemberId { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public string? CancellationReason { get; private set; }

    public void MarkComplete(DateTimeOffset completedAtUtc)
    {
        if (Status != JobStatus.Open)
        {
            throw new JobCompletionRejectedException(Id);
        }

        Status = JobStatus.PendingApproval;
        CompletedAtUtc = completedAtUtc.ToUniversalTime();
    }

    public void Approve(DateTimeOffset approvedAtUtc)
    {
        if (Status != JobStatus.PendingApproval)
        {
            throw new JobApprovalRejectedException(Id);
        }

        Status = JobStatus.Approved;
        ApprovedAtUtc = approvedAtUtc.ToUniversalTime();
    }

    public void Reject()
    {
        if (Status != JobStatus.PendingApproval)
        {
            throw new JobRejectionRejectedException(Id);
        }

        Status = JobStatus.Open;
        CompletedAtUtc = null;
    }

    public void Edit(
        string name,
        string description,
        int points,
        DateOnly scheduledDate,
        AgendaPeriod agendaPeriod,
        TimeOnly? scheduledTime)
    {
        if (Status is not (JobStatus.Open or JobStatus.PendingApproval))
        {
            throw new JobEditRejectedException(Id);
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length == 0 || trimmedName.Length > MaximumNameLength)
        {
            throw new ArgumentException(
                $"A job name must contain between 1 and {MaximumNameLength} characters.",
                nameof(name));
        }

        var trimmedDescription = description.Trim();
        if (trimmedDescription.Length > MaximumDescriptionLength)
        {
            throw new ArgumentException(
                $"A job description cannot exceed {MaximumDescriptionLength} characters.",
                nameof(description));
        }

        if (points < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(points), "Points cannot be negative.");
        }

        Name = trimmedName;
        Description = trimmedDescription;
        Points = points;
        ScheduledDate = scheduledDate;
        AgendaPeriod = agendaPeriod;
        ScheduledTime = scheduledTime;
    }

    public void Cancel(
        Guid cancelledByMemberId,
        DateTimeOffset cancelledAtUtc,
        string? reason)
    {
        if (Status is not (JobStatus.Open or JobStatus.PendingApproval))
        {
            throw new JobCancellationRejectedException(Id);
        }

        if (cancelledByMemberId == Guid.Empty)
        {
            throw new ArgumentException("A cancelling adult is required.", nameof(cancelledByMemberId));
        }

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason?.Length > MaximumCancellationReasonLength)
        {
            throw new ArgumentException(
                $"A cancellation reason cannot exceed {MaximumCancellationReasonLength} characters.",
                nameof(reason));
        }

        Status = JobStatus.Cancelled;
        CancelledByMemberId = cancelledByMemberId;
        CancelledAtUtc = cancelledAtUtc.ToUniversalTime();
        CancellationReason = trimmedReason;
    }

    public void ScheduleFor(DateOnly date)
    {
        ScheduledDate = date;
    }
}
