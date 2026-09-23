using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;

namespace FamilyJobsBoard.Application.Today;

/// <summary>
/// Maps domain job/member state to the read-model shapes shared by the daily agenda and the
/// calendar, so the two views can never disagree about what a job's state means.
/// </summary>
internal static class TodayJobMapping
{
    public static TodayMember MapMember(HouseholdMember member)
    {
        return new TodayMember(
            member.Id,
            member.FirstName,
            member.Nickname,
            member.DisplayName,
            member.IsAdult);
    }

    public static TodayJob MapJob(
        Job job,
        HouseholdMember child,
        TodayJobRejection? latestRejection)
    {
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
            MapStatus(job.Status),
            job.CompletedAtUtc,
            job.ApprovedAtUtc,
            job.Status == JobStatus.Open ? latestRejection : null);
    }

    public static string MapStatus(JobStatus status)
    {
        return status switch
        {
            JobStatus.Open => "open",
            JobStatus.PendingApproval => "pendingApproval",
            JobStatus.Approved => "approved",
            _ => throw new InvalidOperationException($"Unknown job status '{status}'."),
        };
    }

    public static string MapAgendaPeriod(AgendaPeriod agendaPeriod)
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

    public static string MapRecurrenceFrequency(RecurrenceFrequency frequency)
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
