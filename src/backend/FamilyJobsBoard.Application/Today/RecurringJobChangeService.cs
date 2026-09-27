using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FamilyJobsBoard.Application.Clock;
using FamilyJobsBoard.Domain.Jobs;

namespace FamilyJobsBoard.Application.Today;

public sealed class RecurringJobChangeService
{
    private enum ChangeOperation
    {
        Edit,
        Cancel,
    }

    private enum ChangeScope
    {
        ThisOnly,
        AllFuture,
        All,
    }

    private enum PlannedActionKind
    {
        Update,
        Create,
        Cancel,
    }

    private sealed record NormalizedChange(
        ChangeOperation Operation,
        ChangeScope Scope,
        string? Reason,
        int ExpectedSeriesVersion,
        string Name,
        string Description,
        int Points,
        DateOnly ScheduledDate,
        AgendaPeriod AgendaPeriod,
        TimeOnly? ScheduledTime,
        RecurrenceFrequency Frequency,
        IReadOnlyList<DayOfWeek> Weekdays,
        int? DayOfMonth,
        DateOnly? EndDate);

    private sealed record PlannedAction(
        PlannedActionKind Kind,
        Job? Job,
        DateOnly PatternDate,
        DateOnly Date,
        Guid ChildId,
        int Points);

    private sealed record ChangePlan(
        NormalizedChange Change,
        Job Anchor,
        RecurringJobSeries Series,
        IReadOnlyList<PlannedAction> Actions,
        int ApprovedSkippedCount,
        int CancelledSkippedCount,
        int RetrospectivePointIncreaseSkippedCount,
        int NextTurnIndex,
        IReadOnlyList<string> Warnings)
    {
        public RecurringJobChangeImpact Impact => new(
            Actions.Count(action => action.Kind == PlannedActionKind.Update),
            Actions.Count(action => action.Kind == PlannedActionKind.Create),
            Actions.Count(action => action.Kind == PlannedActionKind.Cancel),
            ApprovedSkippedCount,
            CancelledSkippedCount,
            RetrospectivePointIncreaseSkippedCount,
            Warnings);
    }

    private readonly IRecurringJobChangeRepository _repository;
    private readonly IHouseholdClock _clock;

    public RecurringJobChangeService(
        IRecurringJobChangeRepository repository,
        IHouseholdClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<RecurringJobChangePreview> PreviewAsync(
        Guid jobId,
        Guid actorMemberId,
        RecurringJobChangeInput input,
        CancellationToken cancellationToken)
    {
        await EnsureAdultAsync(actorMemberId, cancellationToken);
        var (anchor, series, jobs) = await LoadContextAsync(
            jobId,
            input.ExpectedSeriesVersion,
            false,
            cancellationToken);
        return new RecurringJobChangePreview(
            series.Version,
            PreviewScope(input with { Scope = "thisOnly" }, anchor, series, jobs),
            PreviewScope(input with { Scope = "allFuture" }, anchor, series, jobs),
            PreviewScope(input with { Scope = "all" }, anchor, series, jobs));
    }

    public async Task<RecurringJobChangeResult> ApplyAsync(
        Guid jobId,
        Guid actorMemberId,
        ApplyRecurringJobChange request,
        CancellationToken cancellationToken)
    {
        await EnsureAdultAsync(actorMemberId, cancellationToken);
        if (request.RequestId == Guid.Empty)
        {
            throw Invalid(nameof(request.RequestId), "A request ID is required.");
        }

        var fingerprint = Fingerprint(jobId, actorMemberId, request.Change);
        var existing = await _repository.GetChangeAsync(request.RequestId, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new RecurringJobChangeConflictException(
                    "This request ID was already used for a different recurring job change.");
            }

            return MapResult(existing);
        }

        var plan = await BuildPlanAsync(jobId, request.Change, true, cancellationToken);
        foreach (var action in plan.Actions)
        {
            switch (action.Kind)
            {
                case PlannedActionKind.Update:
                    action.Job!.EditRecurringOccurrence(
                        action.ChildId,
                        plan.Change.Name,
                        plan.Change.Description,
                        action.Points,
                        action.Date,
                        plan.Change.AgendaPeriod,
                        plan.Change.ScheduledTime,
                        plan.Change.Frequency);
                    break;
                case PlannedActionKind.Cancel:
                    action.Job!.Cancel(
                        actorMemberId,
                        _clock.UtcNow,
                        plan.Change.Operation == ChangeOperation.Cancel
                            ? plan.Change.Reason
                            : "Recurring schedule changed.");
                    break;
                case PlannedActionKind.Create:
                    break;
                default:
                    throw new InvalidOperationException("Unknown recurring-job action.");
            }
        }

        var createdJobs = plan.Actions
            .Where(action => action.Kind == PlannedActionKind.Create)
            .Select(action => new Job(
                Guid.NewGuid(),
                action.ChildId,
                plan.Change.Name,
                plan.Change.Description,
                action.Points,
                action.Date,
                plan.Change.AgendaPeriod,
                plan.Change.ScheduledTime,
                plan.Series.Id,
                plan.Change.Frequency))
            .ToArray();
        if (createdJobs.Length > 0)
        {
            await _repository.AddJobsAsync(createdJobs, cancellationToken);
        }

        if (plan.Change.Scope != ChangeScope.ThisOnly)
        {
            if (plan.Change.Operation == ChangeOperation.Cancel)
            {
                var boundary = plan.Change.Scope == ChangeScope.All
                    ? plan.Series.StartDate
                    : PatternDate(plan.Anchor);
                plan.Series.EndBefore(boundary);
            }
            else
            {
                plan.Series.EditSchedule(
                    plan.Change.Name,
                    plan.Change.Description,
                    plan.Change.Points,
                    plan.Change.AgendaPeriod,
                    plan.Change.ScheduledTime,
                    plan.Change.EndDate,
                    plan.Change.Frequency,
                    plan.Change.Weekdays,
                    plan.Change.DayOfMonth,
                    plan.NextTurnIndex);
            }

            await _repository.AddRevisionAsync(
                new RecurringJobSeriesRevision(
                    Guid.NewGuid(),
                    plan.Series.Id,
                    request.RequestId,
                    actorMemberId,
                    _clock.UtcNow,
                    plan.Change.Scope == ChangeScope.All
                        ? plan.Series.StartDate
                        : PatternDate(plan.Anchor),
                    plan.Change.Name,
                    plan.Change.Description,
                    plan.Change.Points,
                    plan.Change.AgendaPeriod,
                    plan.Change.ScheduledTime,
                    plan.Series.EndDate,
                    plan.Change.Frequency,
                    WeekdayMask(plan.Change.Weekdays),
                    plan.Change.DayOfMonth,
                    plan.Change.Operation == ChangeOperation.Cancel),
                cancellationToken);
        }

        var impact = plan.Impact;
        var change = new RecurringJobChange(
            request.RequestId,
            jobId,
            plan.Series.Id,
            actorMemberId,
            fingerprint,
            OperationValue(plan.Change.Operation),
            ScopeValue(plan.Change.Scope),
            impact.UpdatedCount,
            impact.CreatedCount,
            impact.CancelledCount,
            impact.ApprovedSkippedCount,
            impact.CancelledSkippedCount,
            impact.RetrospectivePointIncreaseSkippedCount,
            impact.Warnings,
            plan.Series.Version,
            _clock.UtcNow);
        await _repository.AddChangeAsync(change, cancellationToken);
        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (RecurringJobChangeConcurrentApplyException)
        {
            var concurrentlyApplied = await _repository.GetChangeAsync(request.RequestId, cancellationToken);
            if (concurrentlyApplied is not null
                && string.Equals(concurrentlyApplied.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return MapResult(concurrentlyApplied);
            }

            throw new RecurringJobChangeConflictException(
                "The recurring schedule changed while this request was being applied.");
        }

        return new RecurringJobChangeResult(
            request.RequestId,
            plan.Series.Id,
            plan.Series.Version,
            change.Operation,
            change.Scope,
            impact);
    }

    private async Task<ChangePlan> BuildPlanAsync(
        Guid jobId,
        RecurringJobChangeInput input,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var (anchor, series, jobs) = await LoadContextAsync(
            jobId,
            input.ExpectedSeriesVersion,
            tracking,
            cancellationToken);
        return BuildPlan(input, anchor, series, jobs);
    }

    private async Task<(Job Anchor, RecurringJobSeries Series, IReadOnlyList<Job> Jobs)> LoadContextAsync(
        Guid jobId,
        int expectedSeriesVersion,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var anchor = await _repository.GetJobAsync(jobId, tracking, cancellationToken)
            ?? throw new JobNotFoundException(jobId);
        if (anchor.Status is JobStatus.Approved or JobStatus.Cancelled)
        {
            throw new RecurringJobChangeConflictException(
                "Approved and cancelled jobs cannot be changed.");
        }

        var seriesId = anchor.RecurringJobSeriesId ?? throw new RecurringJobSeriesNotFoundException();
        var series = await _repository.GetSeriesAsync(seriesId, tracking, cancellationToken)
            ?? throw new RecurringJobSeriesNotFoundException();
        if (expectedSeriesVersion != series.Version)
        {
            throw new RecurringJobChangeConflictException(
                "The recurring schedule changed after it was opened. Review the latest schedule and try again.");
        }

        var jobs = await _repository.GetSeriesJobsAsync(series.Id, tracking, cancellationToken);
        return (anchor, series, jobs);
    }

    private ChangePlan BuildPlan(
        RecurringJobChangeInput input,
        Job anchor,
        RecurringJobSeries series,
        IReadOnlyList<Job> jobs)
    {
        var change = Normalize(input, anchor, series);
        return change.Scope == ChangeScope.ThisOnly
            ? PlanThisOnly(change, anchor, series, jobs)
            : PlanSeries(change, anchor, series, jobs);
    }

    private RecurringJobScopePreview PreviewScope(
        RecurringJobChangeInput input,
        Job anchor,
        RecurringJobSeries series,
        IReadOnlyList<Job> jobs)
    {
        try
        {
            return new RecurringJobScopePreview(BuildPlan(input, anchor, series, jobs).Impact, null);
        }
        catch (InvalidRecurringJobChangeException exception)
        {
            return new RecurringJobScopePreview(
                null,
                string.Join(" ", exception.Errors.Values.SelectMany(messages => messages)));
        }
        catch (RecurringJobChangeConflictException exception)
        {
            return new RecurringJobScopePreview(null, exception.Message);
        }
    }

    private ChangePlan PlanThisOnly(
        NormalizedChange change,
        Job anchor,
        RecurringJobSeries series,
        IReadOnlyList<Job> jobs)
    {
        if (change.Operation == ChangeOperation.Cancel)
        {
            return new ChangePlan(
                change,
                anchor,
                series,
                [new PlannedAction(
                    PlannedActionKind.Cancel,
                    anchor,
                    PatternDate(anchor),
                    anchor.ScheduledDate,
                    anchor.ChildId,
                    anchor.Points)],
                0,
                0,
                0,
                series.NextTurnIndex,
                []);
        }

        if (change.ScheduledDate != anchor.ScheduledDate
            && jobs.Any(job => job.Id != anchor.Id && job.ScheduledDate == change.ScheduledDate))
        {
            throw new RecurringJobChangeConflictException(
                "This recurring series already has a job on the selected date.");
        }

        var retrospectiveIncrease = anchor.ScheduledDate < _clock.Today && change.Points > anchor.Points;
        var warnings = new List<string>();
        if (retrospectiveIncrease)
        {
            warnings.Add("The points increase will not be applied because this job is in the past.");
        }

        WarnIfMovedAfterCompletion(anchor, change.ScheduledDate, change.ScheduledTime, warnings);
        return new ChangePlan(
            change,
            anchor,
            series,
            [new PlannedAction(
                PlannedActionKind.Update,
                anchor,
                PatternDate(anchor),
                change.ScheduledDate,
                anchor.ChildId,
                retrospectiveIncrease ? anchor.Points : change.Points)],
            0,
            0,
            retrospectiveIncrease ? 1 : 0,
            series.NextTurnIndex,
            warnings);
    }

    private ChangePlan PlanSeries(
        NormalizedChange change,
        Job anchor,
        RecurringJobSeries series,
        IReadOnlyList<Job> jobs)
    {
        if (change.ScheduledDate != anchor.ScheduledDate)
        {
            throw Invalid(
                nameof(RecurringJobChangeInput.ScheduledDate),
                "Changing one occurrence's date requires This Only; use the recurrence pattern for broader changes.");
        }

        var boundary = change.Scope == ChangeScope.All ? DateOnly.MinValue : PatternDate(anchor);
        var inScope = jobs.Where(job => PatternDate(job) >= boundary)
            .OrderBy(PatternDate)
            .ToArray();
        var approvedSkipped = inScope.Count(job => job.Status == JobStatus.Approved);
        var cancelledSkipped = inScope.Count(job => job.Status == JobStatus.Cancelled);
        var eligible = inScope.Where(job => job.Status is JobStatus.Open or JobStatus.PendingApproval)
            .ToArray();
        var actions = new List<PlannedAction>();
        var warnings = new List<string>();
        var retrospectivePointSkips = 0;

        if (change.Operation == ChangeOperation.Cancel)
        {
            actions.AddRange(eligible.Select(job => new PlannedAction(
                PlannedActionKind.Cancel,
                job,
                PatternDate(job),
                job.ScheduledDate,
                job.ChildId,
                job.Points)));
            return new ChangePlan(
                change,
                anchor,
                series,
                actions,
                approvedSkipped,
                cancelledSkipped,
                0,
                series.NextTurnIndex,
                Warnings(approvedSkipped, cancelledSkipped, 0, warnings));
        }

        foreach (var job in eligible)
        {
            var patternDate = PatternDate(job);
            if (!OccursOn(change, series.StartDate, patternDate))
            {
                actions.Add(new PlannedAction(
                    PlannedActionKind.Cancel,
                    job,
                    patternDate,
                    job.ScheduledDate,
                    job.ChildId,
                    job.Points));
                continue;
            }

            var points = change.Points;
            if (job.ScheduledDate < _clock.Today && points > job.Points)
            {
                points = job.Points;
                retrospectivePointSkips++;
            }

            WarnIfMovedAfterCompletion(job, job.ScheduledDate, change.ScheduledTime, warnings);
            actions.Add(new PlannedAction(
                PlannedActionKind.Update,
                job,
                patternDate,
                job.ScheduledDate,
                job.ChildId,
                points));
        }

        var cursor = StartingTurnIndex(series, jobs, boundary);
        var firstCreationDate = change.Scope == ChangeScope.All
            ? _clock.Today
            : (boundary > _clock.Today ? boundary : _clock.Today);
        var byDate = jobs.GroupBy(PatternDate).ToDictionary(group => group.Key, group => group.ToArray());
        var byScheduledDate = jobs.GroupBy(job => job.ScheduledDate)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var actionIndexByPatternDate = actions
            .Select((action, index) => (action.PatternDate, index))
            .Where(item => actions[item.index].Kind == PlannedActionKind.Update)
            .ToDictionary(item => item.PatternDate, item => item.index);
        var firstReconciledDate = change.Scope == ChangeScope.All ? series.StartDate : boundary;
        for (var date = firstReconciledDate;
             date <= series.GeneratedThrough;
             date = date.AddDays(1))
        {
            if (!OccursOn(change, series.StartDate, date))
            {
                continue;
            }

            byDate.TryGetValue(date, out var existingOnDate);
            var immutable = existingOnDate?.FirstOrDefault(job => job.Status == JobStatus.Approved);
            if (immutable is not null)
            {
                cursor = NextTurnIndexAfter(series, immutable.ChildId, cursor);
                continue;
            }

            if (existingOnDate?.Any(job => job.Status == JobStatus.Cancelled) == true)
            {
                var cancelled = existingOnDate.First(job => job.Status == JobStatus.Cancelled);
                cursor = NextTurnIndexAfter(series, cancelled.ChildId, cursor);
                continue;
            }

            if (actionIndexByPatternDate.TryGetValue(date, out var actionIndex))
            {
                var existingAction = actions[actionIndex];
                if (existingAction.Job!.Status == JobStatus.PendingApproval)
                {
                    cursor = NextTurnIndexAfter(series, existingAction.Job.ChildId, cursor);
                }
                else
                {
                    var childId = TurnChild(series, cursor);
                    actions[actionIndex] = existingAction with { ChildId = childId };
                    cursor = AdvanceTurn(series, cursor);
                }
                continue;
            }

            var movedOccurrence = byScheduledDate.TryGetValue(date, out var jobsScheduledOnDate)
                ? jobsScheduledOnDate.FirstOrDefault(job => PatternDate(job) != date)
                : null;
            if (movedOccurrence is not null)
            {
                cursor = NextTurnIndexAfter(series, movedOccurrence.ChildId, cursor);
                continue;
            }

            if (date >= firstCreationDate && existingOnDate is null)
            {
                var childId = TurnChild(series, cursor);
                actions.Add(new PlannedAction(
                    PlannedActionKind.Create,
                    null,
                    date,
                    date,
                    childId,
                    change.Points));
                cursor = AdvanceTurn(series, cursor);
            }
        }

        return new ChangePlan(
            change,
            anchor,
            series,
            actions,
            approvedSkipped,
            cancelledSkipped,
            retrospectivePointSkips,
            cursor,
            Warnings(approvedSkipped, cancelledSkipped, retrospectivePointSkips, warnings));
    }

    private static NormalizedChange Normalize(
        RecurringJobChangeInput input,
        Job anchor,
        RecurringJobSeries series)
    {
        var errors = new Dictionary<string, string[]>();
        var operation = input.Operation switch
        {
            "edit" => ChangeOperation.Edit,
            "cancel" => ChangeOperation.Cancel,
            _ => default,
        };
        if (input.Operation is not ("edit" or "cancel"))
        {
            errors[nameof(input.Operation)] = ["Choose edit or cancel."];
        }

        var scope = input.Scope switch
        {
            "thisOnly" => ChangeScope.ThisOnly,
            "allFuture" => ChangeScope.AllFuture,
            "all" => ChangeScope.All,
            _ => default,
        };
        if (input.Scope is not ("thisOnly" or "allFuture" or "all"))
        {
            errors[nameof(input.Scope)] = ["Choose This Only, All Future, or All."];
        }

        var reason = NormalizeReason(input.Reason, errors);

        if (operation == ChangeOperation.Cancel)
        {
            if (errors.Count > 0)
            {
                throw new InvalidRecurringJobChangeException(errors);
            }

            return new NormalizedChange(
                operation,
                scope,
                reason,
                input.ExpectedSeriesVersion,
                anchor.Name,
                anchor.Description,
                anchor.Points,
                anchor.ScheduledDate,
                anchor.AgendaPeriod,
                anchor.ScheduledTime,
                series.Frequency,
                series.SelectedWeekdays(),
                series.MonthlyDay,
                series.EndDate);
        }

        var name = input.Name?.Trim() ?? string.Empty;
        var description = input.Description?.Trim() ?? string.Empty;
        if (name.Length is 0 or > Job.MaximumNameLength)
        {
            errors[nameof(input.Name)] = [$"Enter a name of up to {Job.MaximumNameLength} characters."];
        }

        if (description.Length > Job.MaximumDescriptionLength)
        {
            errors[nameof(input.Description)] =
                [$"Enter a description of up to {Job.MaximumDescriptionLength} characters."];
        }

        if (input.Points < 0)
        {
            errors[nameof(input.Points)] = ["Points cannot be negative."];
        }

        if (input.ScheduledDate is null)
        {
            errors[nameof(input.ScheduledDate)] = ["Choose a scheduled date."];
        }

        if (!TryAgendaPeriod(input.AgendaPeriod, out var agendaPeriod))
        {
            errors[nameof(input.AgendaPeriod)] =
                ["Choose morning, arrivingHome, evening, or unscheduled."];
        }

        RecurrenceFrequency frequency;
        IReadOnlyList<DayOfWeek> weekdays;
        int? dayOfMonth;
        DateOnly? endDate;
        if (scope == ChangeScope.ThisOnly)
        {
            frequency = series.Frequency;
            weekdays = series.SelectedWeekdays();
            dayOfMonth = series.MonthlyDay;
            endDate = series.EndDate;
        }
        else
        {
            if (!TryFrequency(input.Frequency, out frequency))
            {
                errors[nameof(input.Frequency)] = ["Choose daily, weekly, or monthly."];
            }

            weekdays = ParseWeekdays(input.Weekdays, errors);
            if (frequency == RecurrenceFrequency.Weekly && weekdays.Count == 0)
            {
                errors[nameof(input.Weekdays)] = ["Choose at least one weekday."];
            }
            else if (frequency != RecurrenceFrequency.Weekly && weekdays.Count > 0)
            {
                errors[nameof(input.Weekdays)] = ["Only weekly jobs select weekdays."];
            }

            dayOfMonth = input.DayOfMonth;
            if (frequency == RecurrenceFrequency.Monthly && dayOfMonth is not (>= 1 and <= 31))
            {
                errors[nameof(input.DayOfMonth)] = ["Choose a day of month from 1 through 31."];
            }
            else if (frequency != RecurrenceFrequency.Monthly && dayOfMonth is not null)
            {
                errors[nameof(input.DayOfMonth)] = ["Only monthly jobs select a day of month."];
            }

            endDate = input.EndDate;
            var minimumEndDate = scope == ChangeScope.AllFuture
                ? PatternDate(anchor)
                : series.StartDate;
            if (endDate is { } selectedEndDate && selectedEndDate < minimumEndDate)
            {
                errors[nameof(input.EndDate)] =
                    [$"The end date must be on or after {minimumEndDate:yyyy-MM-dd}."];
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidRecurringJobChangeException(errors);
        }

        return new NormalizedChange(
            operation,
            scope,
            reason,
            input.ExpectedSeriesVersion,
            name,
            description,
            input.Points,
            input.ScheduledDate!.Value,
            agendaPeriod,
            input.ScheduledTime,
            frequency,
            weekdays,
            dayOfMonth,
            endDate);
    }

    private static IReadOnlyList<DayOfWeek> ParseWeekdays(
        IReadOnlyList<string>? values,
        IDictionary<string, string[]> errors)
    {
        var weekdays = new List<DayOfWeek>();
        foreach (var value in values ?? [])
        {
            if (!Enum.TryParse<DayOfWeek>(value, true, out var weekday)
                || !Enum.IsDefined(weekday))
            {
                errors[nameof(RecurringJobChangeInput.Weekdays)] = ["Choose valid weekdays."];
                return [];
            }

            weekdays.Add(weekday);
        }

        if (weekdays.Count != weekdays.Distinct().Count())
        {
            errors[nameof(RecurringJobChangeInput.Weekdays)] = ["Choose each weekday once."];
            return [];
        }

        return weekdays;
    }

    private static bool OccursOn(NormalizedChange change, DateOnly startDate, DateOnly date)
    {
        if (date < startDate || (change.EndDate is { } endDate && date > endDate))
        {
            return false;
        }

        return change.Frequency switch
        {
            RecurrenceFrequency.Daily => true,
            RecurrenceFrequency.Weekly => change.Weekdays.Contains(date.DayOfWeek),
            RecurrenceFrequency.Monthly =>
                date.Day == Math.Min(change.DayOfMonth!.Value, DateTime.DaysInMonth(date.Year, date.Month)),
            _ => false,
        };
    }

    private static int StartingTurnIndex(
        RecurringJobSeries series,
        IReadOnlyList<Job> jobs,
        DateOnly boundary)
    {
        if (!series.TakesTurns)
        {
            return 0;
        }

        var last = jobs.Where(job => PatternDate(job) < boundary)
            .OrderByDescending(PatternDate)
            .FirstOrDefault();
        return last is null ? 0 : NextTurnIndexAfter(series, last.ChildId, 0);
    }

    private static Guid TurnChild(RecurringJobSeries series, int cursor) =>
        series.TakesTurns ? series.RotationChildIds[cursor] : series.ChildId;

    private static int AdvanceTurn(RecurringJobSeries series, int cursor) =>
        series.TakesTurns ? (cursor + 1) % series.RotationChildIds.Count : 0;

    private static int NextTurnIndexAfter(RecurringJobSeries series, Guid childId, int fallback)
    {
        if (!series.TakesTurns)
        {
            return 0;
        }

        var index = series.RotationChildIds.ToList().IndexOf(childId);
        return index < 0 ? fallback : (index + 1) % series.RotationChildIds.Count;
    }

    private void WarnIfMovedAfterCompletion(
        Job job,
        DateOnly date,
        TimeOnly? time,
        ICollection<string> warnings)
    {
        if (job.CompletedAtUtc is null
            || (date == job.ScheduledDate && time == job.ScheduledTime))
        {
            return;
        }

        var scheduled = _clock.ToUtc(date, time ?? TimeOnly.MinValue);
        if (scheduled > job.CompletedAtUtc.Value)
        {
            warnings.Add($"{job.Name} was completed before its proposed schedule.");
        }
    }

    private static DateOnly PatternDate(Job job) =>
        job.OriginalScheduledDate ?? job.ScheduledDate;

    private static IReadOnlyList<string> Warnings(
        int approved,
        int cancelled,
        int pointSkips,
        List<string> warnings)
    {
        if (approved > 0)
        {
            warnings.Add($"{approved} approved job(s) will remain unchanged.");
        }

        if (cancelled > 0)
        {
            warnings.Add($"{cancelled} already-cancelled job(s) will remain unchanged.");
        }

        if (pointSkips > 0)
        {
            warnings.Add($"{pointSkips} past job(s) will keep their existing points.");
        }

        return warnings.Distinct().ToArray();
    }

    private async Task EnsureAdultAsync(Guid actorMemberId, CancellationToken cancellationToken)
    {
        var actor = await _repository.GetMemberAsync(actorMemberId, cancellationToken);
        if (actor is not { IsAdult: true })
        {
            throw new RecurringJobChangeForbiddenException();
        }
    }

    private static RecurringJobChangeResult MapResult(RecurringJobChange change) => new(
        change.Id,
        change.SeriesId,
        change.SeriesVersion,
        change.Operation,
        change.Scope,
        new RecurringJobChangeImpact(
            change.UpdatedCount,
            change.CreatedCount,
            change.CancelledCount,
            change.ApprovedSkippedCount,
            change.CancelledSkippedCount,
            change.RetrospectivePointIncreaseSkippedCount,
            change.Warnings));

    private static string Fingerprint(
        Guid jobId,
        Guid actorMemberId,
        RecurringJobChangeInput input)
    {
        var value = string.Join('|',
            jobId,
            actorMemberId,
            input.Operation,
            input.Scope,
            input.Reason?.Trim(),
            input.Name?.Trim(),
            input.Description?.Trim(),
            input.Points,
            input.ScheduledDate?.ToString("O", CultureInfo.InvariantCulture),
            input.AgendaPeriod,
            input.ScheduledTime?.ToString("O", CultureInfo.InvariantCulture),
            input.Frequency,
            string.Join(',', input.Weekdays ?? []),
            input.DayOfMonth,
            input.EndDate?.ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static int WeekdayMask(IEnumerable<DayOfWeek> weekdays) =>
        weekdays.Aggregate(0, (mask, weekday) => mask | 1 << (int)weekday);

    private static string OperationValue(ChangeOperation operation) => operation switch
    {
        ChangeOperation.Edit => "edit",
        ChangeOperation.Cancel => "cancel",
        _ => throw new InvalidOperationException(),
    };

    private static string ScopeValue(ChangeScope scope) => scope switch
    {
        ChangeScope.ThisOnly => "thisOnly",
        ChangeScope.AllFuture => "allFuture",
        ChangeScope.All => "all",
        _ => throw new InvalidOperationException(),
    };

    private static bool TryFrequency(string? value, out RecurrenceFrequency frequency)
    {
        frequency = value switch
        {
            "daily" => RecurrenceFrequency.Daily,
            "weekly" => RecurrenceFrequency.Weekly,
            "monthly" => RecurrenceFrequency.Monthly,
            _ => default,
        };
        return value is "daily" or "weekly" or "monthly";
    }

    private static bool TryAgendaPeriod(string? value, out AgendaPeriod period)
    {
        period = value switch
        {
            "morning" => AgendaPeriod.Morning,
            "arrivingHome" => AgendaPeriod.ArrivingHome,
            "evening" => AgendaPeriod.Evening,
            "unscheduled" => AgendaPeriod.Unscheduled,
            _ => default,
        };
        return value is "morning" or "arrivingHome" or "evening" or "unscheduled";
    }

    private static InvalidRecurringJobChangeException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private static string? NormalizeReason(
        string? reason,
        IDictionary<string, string[]> errors)
    {
        var normalized = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (normalized?.Length > Job.MaximumCancellationReasonLength)
        {
            errors[nameof(RecurringJobChangeInput.Reason)] =
                [$"A cancellation reason cannot exceed {Job.MaximumCancellationReasonLength} characters."];
        }

        return normalized;
    }
}
