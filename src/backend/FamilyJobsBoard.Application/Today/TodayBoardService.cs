using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Domain.Points;

namespace FamilyJobsBoard.Application.Today;

public sealed class TodayBoardService
{
    private readonly ITodayBoardRepository _repository;
    private readonly IHouseholdClock _clock;

    public TodayBoardService(ITodayBoardRepository repository, IHouseholdClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<TodayBoard> GetAsync(
        Guid defaultViewerId,
        Guid? viewerId,
        CancellationToken cancellationToken)
    {
        var members = await _repository.GetMembersAsync(cancellationToken);
        if (members.Count == 0)
        {
            throw new TodayBoardNotAvailableException();
        }

        var selectedId = viewerId ?? defaultViewerId;
        var viewer = members.SingleOrDefault(member => member.Id == selectedId)
            ?? throw new HouseholdMemberNotFoundException(selectedId);
        var children = members.Where(member => !member.IsAdult).ToArray();
        var visibleChildren = viewer.IsAdult
            ? children
            : children.Where(child => child.Id == viewer.Id).ToArray();
        var visibleChildIds = visibleChildren.Select(child => child.Id).ToArray();
        var jobs = await _repository.GetJobsAsync(
            visibleChildIds,
            _clock.Today,
            cancellationToken);
        var latestRejections = await _repository.GetLatestRejectionsAsync(
            visibleChildIds,
            _clock.Today,
            cancellationToken);
        var rejectionByJobId = latestRejections.ToDictionary(rejection => rejection.JobId);
        var childById = children.ToDictionary(child => child.Id);
        TodayPointsSummary? points = null;
        if (!viewer.IsAdult)
        {
            points = await _repository.GetPointsSummaryAsync(viewer.Id, cancellationToken);
        }

        return new TodayBoard(
            MapMember(viewer),
            members.Select(MapMember).ToArray(),
            _clock.Today,
            jobs.Select(job => MapJob(
                job,
                childById[job.ChildId],
                rejectionByJobId.GetValueOrDefault(job.Id))).ToArray(),
            points?.Balance,
            points?.Earnings ?? [],
            jobs.Count(job => job.Status == JobStatus.PendingApproval));
    }

    public async Task<TodayJob> CompleteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await GetJobAsync(jobId, cancellationToken);
        var child = await GetChildAsync(job.ChildId, cancellationToken);

        job.MarkComplete(_clock.UtcNow);
        await _repository.SaveChangesAsync(cancellationToken);

        return MapJob(job, child, null);
    }

    public async Task<TodayJob> AddJobAsync(
        AddTodayJob request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var description = request.Description?.Trim() ?? string.Empty;
        var errors = ValidateNewJob(name, description, request.Points);
        var child = await _repository.GetMemberAsync(request.ChildId, cancellationToken);
        if (child is null || child.IsAdult)
        {
            errors[nameof(AddTodayJob.ChildId)] = ["Choose a child in this household."];
        }

        if (errors.Count > 0)
        {
            throw new InvalidTodayJobException(errors);
        }

        var job = new Job(
            Guid.NewGuid(),
            child!.Id,
            name,
            description,
            request.Points,
            _clock.Today);

        await _repository.AddJobAsync(job, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return MapJob(job, child, null);
    }

    public async Task<TodayJobApproval> ApproveAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await GetJobAsync(jobId, cancellationToken);
        var child = await GetChildAsync(job.ChildId, cancellationToken);

        var decidedAtUtc = _clock.UtcNow;
        job.Approve(decidedAtUtc);
        var decision = new JobReviewDecision(
            Guid.NewGuid(),
            job.Id,
            JobReviewOutcome.Approved,
            null,
            decidedAtUtc);
        var award = new PointsLedgerEntry(
            Guid.NewGuid(),
            job.ChildId,
            job.Id,
            job.Points,
            decidedAtUtc);

        await _repository.AddReviewDecisionAsync(decision, cancellationToken);
        await _repository.AddPointsAwardAsync(award, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        var points = await _repository.GetPointsSummaryAsync(
            job.ChildId,
            cancellationToken);

        return new TodayJobApproval(MapJob(job, child, null), points.Balance);
    }

    public async Task<TodayJob> RejectAsync(
        Guid jobId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason?.Length > JobReviewDecision.MaximumReasonLength)
        {
            throw new InvalidJobRejectionException(new Dictionary<string, string[]>
            {
                ["Reason"] =
                    [$"A rejection reason cannot exceed {JobReviewDecision.MaximumReasonLength} characters."],
            });
        }

        var job = await GetJobAsync(jobId, cancellationToken);
        var child = await GetChildAsync(job.ChildId, cancellationToken);
        var decidedAtUtc = _clock.UtcNow;
        job.Reject();
        var decision = new JobReviewDecision(
            Guid.NewGuid(),
            job.Id,
            JobReviewOutcome.Rejected,
            trimmedReason,
            decidedAtUtc);

        await _repository.AddReviewDecisionAsync(decision, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return MapJob(job, child, new TodayJobRejection(
            decision.Id,
            decision.JobId,
            decision.Reason,
            decision.DecidedAtUtc));
    }

    private async Task<Job> GetJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        return await _repository.GetJobAsync(jobId, cancellationToken)
            ?? throw new JobNotFoundException(jobId);
    }

    private async Task<HouseholdMember> GetChildAsync(
        Guid childId,
        CancellationToken cancellationToken)
    {
        var child = await _repository.GetMemberAsync(childId, cancellationToken);
        return child is { IsAdult: false }
            ? child
            : throw new HouseholdMemberNotFoundException(childId);
    }

    private static Dictionary<string, string[]> ValidateNewJob(
        string name,
        string description,
        int points)
    {
        var errors = new Dictionary<string, string[]>();

        if (name.Length == 0)
        {
            errors[nameof(AddTodayJob.Name)] = ["A job name is required."];
        }
        else if (name.Length > Job.MaximumNameLength)
        {
            errors[nameof(AddTodayJob.Name)] =
                [$"A job name cannot exceed {Job.MaximumNameLength} characters."];
        }

        if (description.Length > Job.MaximumDescriptionLength)
        {
            errors[nameof(AddTodayJob.Description)] =
                [$"A job description cannot exceed {Job.MaximumDescriptionLength} characters."];
        }

        if (points < 0)
        {
            errors[nameof(AddTodayJob.Points)] = ["Points cannot be negative."];
        }

        return errors;
    }

    private static TodayMember MapMember(HouseholdMember member)
    {
        return new TodayMember(
            member.Id,
            member.FirstName,
            member.Nickname,
            member.DisplayName,
            member.IsAdult);
    }

    private static TodayJob MapJob(
        Job job,
        HouseholdMember child,
        TodayJobRejection? latestRejection)
    {
        var status = job.Status switch
        {
            JobStatus.Open => "open",
            JobStatus.PendingApproval => "pendingApproval",
            JobStatus.Approved => "approved",
            _ => throw new InvalidOperationException($"Unknown job status '{job.Status}'."),
        };

        return new TodayJob(
            job.Id,
            child.Id,
            child.DisplayName,
            job.Name,
            job.Description,
            job.Points,
            status,
            job.CompletedAtUtc,
            job.ApprovedAtUtc,
            job.Status == JobStatus.Open ? latestRejection : null);
    }
}
