namespace FamilyJobsBoard.Domain.Jobs;

public sealed class RecurringJobChange
{
    private RecurringJobChange()
    {
    }

    public RecurringJobChange(
        Guid requestId,
        Guid anchorJobId,
        Guid seriesId,
        Guid actorMemberId,
        string fingerprint,
        string operation,
        string scope,
        int updatedCount,
        int createdCount,
        int cancelledCount,
        int approvedSkippedCount,
        int cancelledSkippedCount,
        int retrospectivePointIncreaseSkippedCount,
        int seriesVersion,
        DateTimeOffset appliedAtUtc)
    {
        if (requestId == Guid.Empty || anchorJobId == Guid.Empty || seriesId == Guid.Empty
            || actorMemberId == Guid.Empty)
        {
            throw new ArgumentException("A recurring job change needs request, job, series, and actor IDs.");
        }

        Id = requestId;
        AnchorJobId = anchorJobId;
        SeriesId = seriesId;
        ActorMemberId = actorMemberId;
        Fingerprint = fingerprint;
        Operation = operation;
        Scope = scope;
        UpdatedCount = updatedCount;
        CreatedCount = createdCount;
        CancelledCount = cancelledCount;
        ApprovedSkippedCount = approvedSkippedCount;
        CancelledSkippedCount = cancelledSkippedCount;
        RetrospectivePointIncreaseSkippedCount = retrospectivePointIncreaseSkippedCount;
        SeriesVersion = seriesVersion;
        AppliedAtUtc = appliedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid AnchorJobId { get; private set; }
    public Guid SeriesId { get; private set; }
    public Guid ActorMemberId { get; private set; }
    public string Fingerprint { get; private set; } = string.Empty;
    public string Operation { get; private set; } = string.Empty;
    public string Scope { get; private set; } = string.Empty;
    public int UpdatedCount { get; private set; }
    public int CreatedCount { get; private set; }
    public int CancelledCount { get; private set; }
    public int ApprovedSkippedCount { get; private set; }
    public int CancelledSkippedCount { get; private set; }
    public int RetrospectivePointIncreaseSkippedCount { get; private set; }
    public int SeriesVersion { get; private set; }
    public DateTimeOffset AppliedAtUtc { get; private set; }
}
