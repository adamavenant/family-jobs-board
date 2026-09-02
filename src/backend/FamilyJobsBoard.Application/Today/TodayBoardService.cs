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
        await EnsureRecurringJobsAsync(cancellationToken);
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

    public async Task<RecurringJobCreation> CreateDailyRecurringJobAsync(
        CreateDailyRecurringJob request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var description = request.Description?.Trim() ?? string.Empty;
        var errors = ValidateNewJob(name, description, request.Points);
        if (request.RequestId == Guid.Empty)
        {
            errors[nameof(CreateDailyRecurringJob.RequestId)] = ["A request ID is required."];
        }

        var viewer = await _repository.GetMemberAsync(request.ViewerId, cancellationToken);
        if (viewer is null || !viewer.IsAdult)
        {
            errors[nameof(CreateDailyRecurringJob.ViewerId)] =
                ["Only an adult in this household can create recurring jobs."];
        }

        var child = await _repository.GetMemberAsync(request.ChildId, cancellationToken);
        if (child is null || child.IsAdult)
        {
            errors[nameof(CreateDailyRecurringJob.ChildId)] = ["Choose a child in this household."];
        }

        if (!TryParseAgendaPeriod(request.AgendaPeriod, out var agendaPeriod))
        {
            errors[nameof(CreateDailyRecurringJob.AgendaPeriod)] =
                ["Choose morning, arrivingHome, evening, or unscheduled."];
        }

        if (request.EndDate < request.StartDate)
        {
            errors[nameof(CreateDailyRecurringJob.EndDate)] =
                ["The end date cannot precede the start date."];
        }

        if (request.StartDate < _clock.Today)
        {
            errors[nameof(CreateDailyRecurringJob.StartDate)] =
                ["The start date cannot be in the past."];
        }

        if (errors.Count > 0)
        {
            throw new InvalidDailyRecurringJobException(errors);
        }

        var horizon = _clock.Today.AddDays(55);
        var existing = await _repository.GetRecurringJobSeriesAsync(
            request.RequestId,
            cancellationToken);
        if (existing is not null)
        {
            if (!existing.MatchesDaily(
                    child!.Id,
                    viewer!.Id,
                    name,
                    description,
                    request.Points,
                    agendaPeriod,
                    request.ScheduledTime,
                    request.StartDate,
                    request.EndDate))
            {
                throw new DailyRecurringJobRequestConflictException(request.RequestId);
            }

            var existingCount = await _repository.GetRecurringJobSeriesOccurrenceCountAsync(
                existing.Id,
                cancellationToken);
            return new RecurringJobCreation(
                existing.Id,
                existing.GeneratedThrough,
                existingCount,
                false);
        }

        var series = RecurringJobSeries.Daily(
            request.RequestId,
            child!.Id,
            viewer!.Id,
            name,
            description,
            request.Points,
            agendaPeriod,
            request.ScheduledTime,
            request.StartDate,
            request.EndDate);
        var occurrences = series
            .GenerateThrough(horizon)
            .Select(date => CreateOccurrence(series, date))
            .ToArray();

        await _repository.AddRecurringJobSeriesAsync(series, cancellationToken);
        await _repository.AddJobsAsync(occurrences, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new RecurringJobCreation(
            series.Id,
            series.GeneratedThrough,
            occurrences.Length,
            true);
    }

    public async Task<RecurringJobCreation> CreateWeeklyRecurringJobAsync(
        CreateWeeklyRecurringJob request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var description = request.Description?.Trim() ?? string.Empty;
        var errors = ValidateNewJob(name, description, request.Points);
        if (request.RequestId == Guid.Empty)
        {
            errors[nameof(CreateWeeklyRecurringJob.RequestId)] = ["A request ID is required."];
        }

        var viewer = await _repository.GetMemberAsync(request.ViewerId, cancellationToken);
        if (viewer is null || !viewer.IsAdult)
        {
            errors[nameof(CreateWeeklyRecurringJob.ViewerId)] =
                ["Only an adult in this household can create recurring jobs."];
        }

        var child = await _repository.GetMemberAsync(request.ChildId, cancellationToken);
        if (child is null || child.IsAdult)
        {
            errors[nameof(CreateWeeklyRecurringJob.ChildId)] = ["Choose a child in this household."];
        }

        if (!TryParseAgendaPeriod(request.AgendaPeriod, out var agendaPeriod))
        {
            errors[nameof(CreateWeeklyRecurringJob.AgendaPeriod)] =
                ["Choose morning, arrivingHome, evening, or unscheduled."];
        }

        if (!TryParseWeekdays(request.Weekdays, out var weekdays))
        {
            errors[nameof(CreateWeeklyRecurringJob.Weekdays)] =
                ["Choose one or more weekdays without duplicates."];
        }

        if (request.EndDate < request.StartDate)
        {
            errors[nameof(CreateWeeklyRecurringJob.EndDate)] =
                ["The end date cannot precede the start date."];
        }

        if (request.StartDate < _clock.Today)
        {
            errors[nameof(CreateWeeklyRecurringJob.StartDate)] =
                ["The start date cannot be in the past."];
        }

        if (errors.Count > 0)
        {
            throw new InvalidWeeklyRecurringJobException(errors);
        }

        var horizon = _clock.Today.AddDays(55);
        var existing = await _repository.GetRecurringJobSeriesAsync(
            request.RequestId,
            cancellationToken);
        if (existing is not null)
        {
            if (!existing.MatchesWeekly(
                    child!.Id,
                    viewer!.Id,
                    name,
                    description,
                    request.Points,
                    agendaPeriod,
                    request.ScheduledTime,
                    request.StartDate,
                    request.EndDate,
                    weekdays))
            {
                throw new WeeklyRecurringJobRequestConflictException(request.RequestId);
            }

            var existingCount = await _repository.GetRecurringJobSeriesOccurrenceCountAsync(
                existing.Id,
                cancellationToken);
            return new RecurringJobCreation(
                existing.Id,
                existing.GeneratedThrough,
                existingCount,
                false);
        }

        var series = RecurringJobSeries.Weekly(
            request.RequestId,
            child!.Id,
            viewer!.Id,
            name,
            description,
            request.Points,
            agendaPeriod,
            request.ScheduledTime,
            request.StartDate,
            request.EndDate,
            weekdays);
        var occurrences = series
            .GenerateThrough(horizon)
            .Select(date => CreateOccurrence(series, date))
            .ToArray();

        await _repository.AddRecurringJobSeriesAsync(series, cancellationToken);
        await _repository.AddJobsAsync(occurrences, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new RecurringJobCreation(
            series.Id,
            series.GeneratedThrough,
            occurrences.Length,
            true);
    }

    public async Task<RecurringJobCreation> CreateMonthlyRecurringJobAsync(
        CreateMonthlyRecurringJob request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var description = request.Description?.Trim() ?? string.Empty;
        var errors = ValidateNewJob(name, description, request.Points);
        if (request.RequestId == Guid.Empty)
        {
            errors[nameof(CreateMonthlyRecurringJob.RequestId)] = ["A request ID is required."];
        }

        var viewer = await _repository.GetMemberAsync(request.ViewerId, cancellationToken);
        if (viewer is null || !viewer.IsAdult)
        {
            errors[nameof(CreateMonthlyRecurringJob.ViewerId)] =
                ["Only an adult in this household can create recurring jobs."];
        }

        var child = await _repository.GetMemberAsync(request.ChildId, cancellationToken);
        if (child is null || child.IsAdult)
        {
            errors[nameof(CreateMonthlyRecurringJob.ChildId)] = ["Choose a child in this household."];
        }

        if (!TryParseAgendaPeriod(request.AgendaPeriod, out var agendaPeriod))
        {
            errors[nameof(CreateMonthlyRecurringJob.AgendaPeriod)] =
                ["Choose morning, arrivingHome, evening, or unscheduled."];
        }

        if (request.DayOfMonth is < 1 or > 31)
        {
            errors[nameof(CreateMonthlyRecurringJob.DayOfMonth)] =
                ["Choose a day of month from 1 through 31."];
        }

        if (request.EndDate < request.StartDate)
        {
            errors[nameof(CreateMonthlyRecurringJob.EndDate)] =
                ["The end date cannot precede the start date."];
        }

        if (request.StartDate < _clock.Today)
        {
            errors[nameof(CreateMonthlyRecurringJob.StartDate)] =
                ["The start date cannot be in the past."];
        }

        if (errors.Count > 0)
        {
            throw new InvalidMonthlyRecurringJobException(errors);
        }

        var horizon = _clock.Today.AddDays(55);
        var existing = await _repository.GetRecurringJobSeriesAsync(
            request.RequestId,
            cancellationToken);
        if (existing is not null)
        {
            if (!existing.MatchesMonthly(
                    child!.Id,
                    viewer!.Id,
                    name,
                    description,
                    request.Points,
                    agendaPeriod,
                    request.ScheduledTime,
                    request.StartDate,
                    request.EndDate,
                    request.DayOfMonth))
            {
                throw new MonthlyRecurringJobRequestConflictException(request.RequestId);
            }

            var existingCount = await _repository.GetRecurringJobSeriesOccurrenceCountAsync(
                existing.Id,
                cancellationToken);
            return new RecurringJobCreation(
                existing.Id,
                existing.GeneratedThrough,
                existingCount,
                false);
        }

        var series = RecurringJobSeries.Monthly(
            request.RequestId,
            child!.Id,
            viewer!.Id,
            name,
            description,
            request.Points,
            agendaPeriod,
            request.ScheduledTime,
            request.StartDate,
            request.EndDate,
            request.DayOfMonth);
        var occurrences = series
            .GenerateThrough(horizon)
            .Select(date => CreateOccurrence(series, date))
            .ToArray();

        await _repository.AddRecurringJobSeriesAsync(series, cancellationToken);
        await _repository.AddJobsAsync(occurrences, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new RecurringJobCreation(
            series.Id,
            series.GeneratedThrough,
            occurrences.Length,
            true);
    }

    private async Task EnsureRecurringJobsAsync(CancellationToken cancellationToken)
    {
        var horizon = _clock.Today.AddDays(55);
        var seriesToAdvance = await _repository.GetRecurringJobSeriesNeedingGenerationAsync(
            horizon,
            cancellationToken);
        var occurrences = seriesToAdvance
            .SelectMany(series => series
                .GenerateThrough(horizon)
                .Select(date => CreateOccurrence(series, date)))
            .ToArray();
        if (seriesToAdvance.Count == 0)
        {
            return;
        }

        await _repository.AddJobsAsync(occurrences, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    private static Job CreateOccurrence(RecurringJobSeries series, DateOnly date)
    {
        return new Job(
            Guid.NewGuid(),
            series.ChildId,
            series.Name,
            series.Description,
            series.Points,
            date,
            series.AgendaPeriod,
            series.ScheduledTime,
            series.Id,
            series.Frequency);
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

    private static bool TryParseAgendaPeriod(string? value, out AgendaPeriod agendaPeriod)
    {
        agendaPeriod = value switch
        {
            "morning" => AgendaPeriod.Morning,
            "arrivingHome" => AgendaPeriod.ArrivingHome,
            "evening" => AgendaPeriod.Evening,
            "unscheduled" => AgendaPeriod.Unscheduled,
            _ => AgendaPeriod.Unscheduled,
        };
        return value is "morning" or "arrivingHome" or "evening" or "unscheduled";
    }

    private static bool TryParseWeekdays(
        IReadOnlyCollection<string>? values,
        out IReadOnlyCollection<DayOfWeek> weekdays)
    {
        var parsed = new List<DayOfWeek>();
        if (values is null || values.Count == 0)
        {
            weekdays = parsed;
            return false;
        }

        foreach (var value in values)
        {
            if (!Enum.TryParse<DayOfWeek>(value, true, out var weekday)
                || !Enum.IsDefined(weekday))
            {
                weekdays = parsed;
                return false;
            }

            parsed.Add(weekday);
        }

        weekdays = parsed;
        return parsed.Count == parsed.Distinct().Count();
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
            job.ScheduledDate,
            MapAgendaPeriod(job.AgendaPeriod),
            job.ScheduledTime,
            job.RecurringJobSeriesId,
            job.RecurrenceFrequency is null
                ? null
                : MapRecurrenceFrequency(job.RecurrenceFrequency.Value),
            status,
            job.CompletedAtUtc,
            job.ApprovedAtUtc,
            job.Status == JobStatus.Open ? latestRejection : null);
    }

    private static string MapAgendaPeriod(AgendaPeriod agendaPeriod)
    {
        return agendaPeriod switch
        {
            AgendaPeriod.Morning => "morning",
            AgendaPeriod.ArrivingHome => "arrivingHome",
            AgendaPeriod.Evening => "evening",
            AgendaPeriod.Unscheduled => "unscheduled",
            _ => throw new InvalidOperationException($"Unknown agenda period '{agendaPeriod}'."),
        };
    }

    private static string MapRecurrenceFrequency(RecurrenceFrequency frequency)
    {
        return frequency switch
        {
            RecurrenceFrequency.Daily => "daily",
            RecurrenceFrequency.Weekly => "weekly",
            RecurrenceFrequency.Monthly => "monthly",
            _ => throw new InvalidOperationException($"Unknown recurrence frequency '{frequency}'."),
        };
    }
}
