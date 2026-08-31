namespace FamilyJobsBoard.Domain.Jobs;

public sealed class Job
{
    public const int MaximumNameLength = 160;
    public const int MaximumDescriptionLength = 1000;

    private Job()
    {
    }

    public Job(
        Guid id,
        Guid childId,
        string name,
        string description,
        int points,
        DateOnly scheduledDate)
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

        Id = id;
        ChildId = childId;
        Name = trimmedName;
        Description = trimmedDescription;
        Points = points;
        ScheduledDate = scheduledDate;
        Status = JobStatus.Open;
    }

    public Guid Id { get; private set; }

    public Guid ChildId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public int Points { get; private set; }

    public DateOnly ScheduledDate { get; private set; }

    public JobStatus Status { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public DateTimeOffset? ApprovedAtUtc { get; private set; }

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

    public void ScheduleFor(DateOnly date)
    {
        ScheduledDate = date;
    }
}
