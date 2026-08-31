using FamilyJobsBoard.Application.Clock;
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

    public async Task<TodayBoard> GetAsync(CancellationToken cancellationToken)
    {
        var child = await _repository.GetDemoChildAsync(cancellationToken)
            ?? throw new TodayBoardNotAvailableException();
        var jobs = await _repository.GetJobsAsync(child.Id, _clock.Today, cancellationToken);
        var points = await _repository.GetPointsSummaryAsync(child.Id, cancellationToken);

        return new TodayBoard(
            child.Id,
            child.FirstName,
            child.Nickname,
            child.DisplayName,
            points.Balance,
            _clock.Today,
            jobs.Select(MapJob).ToArray(),
            points.Earnings);
    }

    public async Task<TodayJob> CompleteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _repository.GetJobAsync(jobId, cancellationToken)
            ?? throw new JobNotFoundException(jobId);

        job.MarkComplete(_clock.UtcNow);
        await _repository.SaveChangesAsync(cancellationToken);

        return MapJob(job);
    }

    public async Task<TodayJob> AddJobAsync(
        AddTodayJob request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var description = request.Description?.Trim() ?? string.Empty;
        var errors = ValidateNewJob(name, description, request.Points);
        if (errors.Count > 0)
        {
            throw new InvalidTodayJobException(errors);
        }

        var child = await _repository.GetDemoChildAsync(cancellationToken)
            ?? throw new TodayBoardNotAvailableException();

        var job = new Job(
            Guid.NewGuid(),
            child.Id,
            name,
            description,
            request.Points,
            _clock.Today);

        await _repository.AddJobAsync(job, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return MapJob(job);
    }

    public async Task<TodayJobApproval> ApproveAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await _repository.GetJobAsync(jobId, cancellationToken)
            ?? throw new JobNotFoundException(jobId);

        job.Approve(_clock.UtcNow);
        var award = new PointsLedgerEntry(
            Guid.NewGuid(),
            job.ChildId,
            job.Id,
            job.Points,
            _clock.UtcNow);

        await _repository.AddPointsAwardAsync(award, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        var points = await _repository.GetPointsSummaryAsync(
            job.ChildId,
            cancellationToken);

        return new TodayJobApproval(MapJob(job), points.Balance);
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

    private static TodayJob MapJob(Job job)
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
            job.Name,
            job.Description,
            job.Points,
            status,
            job.CompletedAtUtc,
            job.ApprovedAtUtc);
    }
}
