using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Domain.Jobs;

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

        return new TodayBoard(
            child.Id,
            child.FirstName,
            _clock.Today,
            jobs.Select(MapJob).ToArray());
    }

    public async Task<TodayJob> CompleteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _repository.GetJobAsync(jobId, cancellationToken)
            ?? throw new JobNotFoundException(jobId);

        job.MarkComplete(_clock.UtcNow);
        await _repository.SaveChangesAsync(cancellationToken);

        return MapJob(job);
    }

    public async Task<TodayJob> AddJobAsync(string name, string description, int points, CancellationToken cancellationToken)
    {
        // Validate input parameters
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Job name is required.", nameof(name));
            
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Job description is required.", nameof(description));
            
        if (points < 0)
            throw new ArgumentException("Job points cannot be negative.", nameof(points));

        var child = await _repository.GetDemoChildAsync(cancellationToken)
            ?? throw new TodayBoardNotAvailableException();
            
        var job = new Job(
            Guid.NewGuid(),
            child.Id,
            name,
            description,
            points,
            _clock.Today);
        
        await _repository.AddJobAsync(child.Id, job, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return MapJob(job);
    }

    private static TodayJob MapJob(Job job)
    {
        return new TodayJob(
            job.Id,
            job.Name,
            job.Description,
            job.Points,
            job.Status == JobStatus.Open ? "open" : "pendingApproval",
            job.CompletedAtUtc);
    }
}
