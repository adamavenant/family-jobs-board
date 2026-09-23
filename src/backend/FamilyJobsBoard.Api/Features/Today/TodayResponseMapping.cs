using FamilyJobsBoard.Application.Today;

namespace FamilyJobsBoard.Api.Features.Today;

/// <summary>
/// Maps Application-layer Today read models to their wire shapes. Shared by the daily agenda
/// and the calendar endpoints so a job or member can never be represented differently between
/// the two.
/// </summary>
internal static class TodayResponseMapping
{
    public static MemberResponse MapMember(TodayMember member)
    {
        return new MemberResponse(
            member.Id,
            member.FirstName,
            member.Nickname,
            member.DisplayName,
            member.IsAdult);
    }

    public static JobResponse MapJob(TodayJob job)
    {
        return new JobResponse(
            job.Id,
            job.ChildId,
            job.ChildDisplayName,
            job.Name,
            job.Description,
            job.Points,
            job.ScheduledDate,
            job.AgendaPeriod,
            job.ScheduledTime,
            job.RecurringJobSeriesId,
            job.RecurrenceFrequency,
            job.Status,
            job.CompletedAtUtc,
            job.ApprovedAtUtc,
            job.LatestRejection is null
                ? null
                : new JobRejectionResponse(
                    job.LatestRejection.DecisionId,
                    job.LatestRejection.Reason,
                    job.LatestRejection.RejectedAtUtc));
    }
}
