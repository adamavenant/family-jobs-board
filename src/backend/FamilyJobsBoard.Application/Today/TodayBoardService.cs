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

    public DateOnly CurrentDate => _clock.Today;

    public async Task<TodayBoard> GetAsync(Guid viewerId, CancellationToken cancellationToken)
    {
        return await GetAsync(viewerId, _clock.Today, cancellationToken);
    }

    public async Task<TodayBoard> GetAsync(
        Guid viewerId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        await EnsureRecurringJobsAsync(date, cancellationToken);
        var members = await _repository.GetMembersAsync(cancellationToken);
        if (members.Count == 0)
        {
            throw new TodayBoardNotAvailableException();
        }

        var viewer = members.SingleOrDefault(member => member.Id == viewerId)
            ?? throw new HouseholdMemberNotFoundException(viewerId);
        var children = members.Where(member => !member.IsAdult).ToArray();
        var visibleChildren = viewer.IsAdult
            ? children
            : children.Where(child => child.Id == viewer.Id).ToArray();
        var visibleChildIds = visibleChildren.Select(child => child.Id).ToArray();
        var jobs = await _repository.GetJobsAsync(
            visibleChildIds,
            date,
            cancellationToken);
        var latestRejections = await _repository.GetLatestRejectionsAsync(
            visibleChildIds,
            date,
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
            date,
            _clock.Today,
            jobs.Select(job => MapJob(
                job,
                childById[job.ChildId],
                rejectionByJobId.GetValueOrDefault(job.Id))).ToArray(),
            points?.Balance,
            points?.Earnings ?? [],
            jobs.Count(job => job.Status == JobStatus.PendingApproval));
    }

    public async Task<TodayJob> CompleteAsync(
        Guid jobId,
        Guid childId,
        CancellationToken cancellationToken)
    {
        var job = await GetJobAsync(jobId, cancellationToken);
        if (job.ChildId != childId)
        {
            throw new JobOwnershipRejectedException();
        }

        var child = await GetChildAsync(job.ChildId, cancellationToken);

        job.MarkComplete(_clock.UtcNow);
        await _repository.SaveChangesAsync(cancellationToken);

        return MapJob(job, child, null);
    }

    public async Task<IReadOnlyList<TodayJob>> AddJobAsync(
        AddTodayJob request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var description = request.Description?.Trim() ?? string.Empty;
        var errors = ValidateNewJob(name, description, request.Points);
        if (request.ScheduledDate is null)
        {
            errors[nameof(AddTodayJob.ScheduledDate)] = ["Choose a scheduled date."];
        }
        else if (request.ScheduledDate < _clock.Today)
        {
            errors[nameof(AddTodayJob.ScheduledDate)] =
                ["The scheduled date cannot be in the past."];
        }

        if (!TryParseAgendaPeriod(request.AgendaPeriod, out var agendaPeriod))
        {
            errors[nameof(AddTodayJob.AgendaPeriod)] =
                ["Choose morning, arrivingHome, evening, or unscheduled."];
        }

        var children = await GetSelectedChildrenAsync(
            request.ChildIds,
            errors,
            nameof(AddTodayJob.ChildIds),
            cancellationToken);

        if (errors.Count > 0)
        {
            throw new InvalidTodayJobException(errors);
        }

        var jobs = children
            .Select(child => new Job(
                Guid.NewGuid(),
                child.Id,
                name,
                description,
                request.Points,
                request.ScheduledDate!.Value,
                agendaPeriod,
                request.ScheduledTime))
            .ToArray();

        await _repository.AddJobsAsync(jobs, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var childById = children.ToDictionary(child => child.Id);
        return jobs
            .Select(job => MapJob(job, childById[job.ChildId], null))
            .ToArray();
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

        var children = await GetSelectedChildrenAsync(
            request.ChildIds,
            errors,
            nameof(CreateDailyRecurringJob.ChildIds),
            cancellationToken);

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
        var existing = await GetExistingRecurringCreationAsync(
            request.RequestId,
            children,
            series => series.MatchesDaily(
                series.ChildId,
                viewer!.Id,
                name,
                description,
                request.Points,
                agendaPeriod,
                request.ScheduledTime,
                request.StartDate,
                request.EndDate),
            id => new DailyRecurringJobRequestConflictException(id),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var series = children
            .Select(child => RecurringJobSeries.Daily(
                Guid.NewGuid(),
                child.Id,
                viewer!.Id,
                name,
                description,
                request.Points,
                agendaPeriod,
                request.ScheduledTime,
                request.StartDate,
                request.EndDate,
                request.RequestId))
            .ToArray();
        var occurrences = series
            .SelectMany(item => item
                .GenerateThrough(horizon)
                .Select(date => CreateOccurrence(item, date)))
            .ToArray();

        await _repository.AddRecurringJobSeriesAsync(series, cancellationToken);
        await _repository.AddJobsAsync(occurrences, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new RecurringJobCreation(
            series.Select(item => new RecurringJobAssignment(
                item.Id,
                item.ChildId,
                item.GeneratedThrough,
                occurrences.Count(job => job.RecurringJobSeriesId == item.Id)))
                .ToArray(),
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

        var children = await GetSelectedChildrenAsync(
            request.ChildIds,
            errors,
            nameof(CreateWeeklyRecurringJob.ChildIds),
            cancellationToken);

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
        var existing = await GetExistingRecurringCreationAsync(
            request.RequestId,
            children,
            series => series.MatchesWeekly(
                series.ChildId,
                viewer!.Id,
                name,
                description,
                request.Points,
                agendaPeriod,
                request.ScheduledTime,
                request.StartDate,
                request.EndDate,
                weekdays),
            id => new WeeklyRecurringJobRequestConflictException(id),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var series = children
            .Select(child => RecurringJobSeries.Weekly(
                Guid.NewGuid(),
                child.Id,
                viewer!.Id,
                name,
                description,
                request.Points,
                agendaPeriod,
                request.ScheduledTime,
                request.StartDate,
                request.EndDate,
                weekdays,
                request.RequestId))
            .ToArray();
        var occurrences = series
            .SelectMany(item => item
                .GenerateThrough(horizon)
                .Select(date => CreateOccurrence(item, date)))
            .ToArray();

        await _repository.AddRecurringJobSeriesAsync(series, cancellationToken);
        await _repository.AddJobsAsync(occurrences, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new RecurringJobCreation(
            series.Select(item => new RecurringJobAssignment(
                item.Id,
                item.ChildId,
                item.GeneratedThrough,
                occurrences.Count(job => job.RecurringJobSeriesId == item.Id)))
                .ToArray(),
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

        var children = await GetSelectedChildrenAsync(
            request.ChildIds,
            errors,
            nameof(CreateMonthlyRecurringJob.ChildIds),
            cancellationToken);

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
        var existing = await GetExistingRecurringCreationAsync(
            request.RequestId,
            children,
            series => series.MatchesMonthly(
                series.ChildId,
                viewer!.Id,
                name,
                description,
                request.Points,
                agendaPeriod,
                request.ScheduledTime,
                request.StartDate,
                request.EndDate,
                request.DayOfMonth),
            id => new MonthlyRecurringJobRequestConflictException(id),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var series = children
            .Select(child => RecurringJobSeries.Monthly(
                Guid.NewGuid(),
                child.Id,
                viewer!.Id,
                name,
                description,
                request.Points,
                agendaPeriod,
                request.ScheduledTime,
                request.StartDate,
                request.EndDate,
                request.DayOfMonth,
                request.RequestId))
            .ToArray();
        var occurrences = series
            .SelectMany(item => item
                .GenerateThrough(horizon)
                .Select(date => CreateOccurrence(item, date)))
            .ToArray();

        await _repository.AddRecurringJobSeriesAsync(series, cancellationToken);
        await _repository.AddJobsAsync(occurrences, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new RecurringJobCreation(
            series.Select(item => new RecurringJobAssignment(
                item.Id,
                item.ChildId,
                item.GeneratedThrough,
                occurrences.Count(job => job.RecurringJobSeriesId == item.Id)))
                .ToArray(),
            true);
    }

    private async Task<IReadOnlyList<HouseholdMember>> GetSelectedChildrenAsync(
        IReadOnlyCollection<Guid>? childIds,
        IDictionary<string, string[]> errors,
        string errorKey,
        CancellationToken cancellationToken)
    {
        var submittedIds = childIds?.ToArray() ?? [];
        if (submittedIds.Length == 0
            || submittedIds.Any(id => id == Guid.Empty)
            || submittedIds.Distinct().Count() != submittedIds.Length)
        {
            errors[errorKey] = ["Choose one or more children without duplicates."];
            return [];
        }

        var members = await _repository.GetMembersAsync(cancellationToken);
        var childById = members
            .Where(member => !member.IsAdult && member.IsActive)
            .ToDictionary(member => member.Id);
        if (submittedIds.Any(id => !childById.ContainsKey(id)))
        {
            errors[errorKey] = ["Choose active children in this household."];
            return [];
        }

        return submittedIds.Select(id => childById[id]).ToArray();
    }

    private async Task<RecurringJobCreation?> GetExistingRecurringCreationAsync(
        Guid requestId,
        IReadOnlyCollection<HouseholdMember> children,
        Func<RecurringJobSeries, bool> matches,
        Func<Guid, Exception> conflict,
        CancellationToken cancellationToken)
    {
        var existing = await _repository.GetRecurringJobSeriesByRequestAsync(
            requestId,
            cancellationToken);
        if (existing.Count == 0)
        {
            return null;
        }

        var requestedChildIds = children.Select(child => child.Id).ToHashSet();
        if (existing.Count != requestedChildIds.Count
            || existing.Any(series =>
                !requestedChildIds.Contains(series.ChildId) || !matches(series)))
        {
            throw conflict(requestId);
        }

        var existingByChildId = existing.ToDictionary(series => series.ChildId);
        var assignments = new List<RecurringJobAssignment>(children.Count);
        foreach (var child in children)
        {
            var series = existingByChildId[child.Id];
            var occurrenceCount = await _repository.GetRecurringJobSeriesOccurrenceCountAsync(
                series.Id,
                cancellationToken);
            assignments.Add(new RecurringJobAssignment(
                series.Id,
                series.ChildId,
                series.GeneratedThrough,
                occurrenceCount));
        }

        return new RecurringJobCreation(assignments, false);
    }

    private async Task EnsureRecurringJobsAsync(
        DateOnly requestedDate,
        CancellationToken cancellationToken)
    {
        var rollingHorizon = _clock.Today.AddDays(55);
        var horizon = requestedDate > rollingHorizon ? requestedDate : rollingHorizon;
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
