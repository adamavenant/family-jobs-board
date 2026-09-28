using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class RecurringJobSeriesRevisionConfiguration
    : IEntityTypeConfiguration<RecurringJobSeriesRevision>
{
    public void Configure(EntityTypeBuilder<RecurringJobSeriesRevision> builder)
    {
        builder.ToTable("recurring_job_series_revisions");
        builder.HasKey(revision => revision.Id);
        builder.Property(revision => revision.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(revision => revision.SeriesId).HasColumnName("series_id");
        builder.Property(revision => revision.ChangeRequestId).HasColumnName("change_request_id");
        builder.Property(revision => revision.CreatedByAdultId).HasColumnName("created_by_adult_id");
        builder.Property(revision => revision.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(revision => revision.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(revision => revision.Name).HasColumnName("name")
            .HasMaxLength(Job.MaximumNameLength);
        builder.Property(revision => revision.Description).HasColumnName("description")
            .HasMaxLength(Job.MaximumDescriptionLength);
        builder.Property(revision => revision.Points).HasColumnName("points");
        builder.Property(revision => revision.AgendaPeriod).HasColumnName("agenda_period")
            .HasConversion<string>().HasMaxLength(32);
        builder.Property(revision => revision.ScheduledTime).HasColumnName("scheduled_time");
        builder.Property(revision => revision.EndDate).HasColumnName("end_date");
        builder.Property(revision => revision.Frequency).HasColumnName("frequency")
            .HasConversion<string>().HasMaxLength(16);
        builder.Property(revision => revision.WeekdayMask).HasColumnName("weekday_mask");
        builder.Property(revision => revision.MonthlyDay).HasColumnName("monthly_day");
        builder.Property(revision => revision.Ended).HasColumnName("ended");
        builder.HasIndex(revision => revision.ChangeRequestId).IsUnique();
        builder.HasIndex(revision => new { revision.SeriesId, revision.EffectiveFrom });
        builder.HasOne<RecurringJobSeries>().WithMany().HasForeignKey(revision => revision.SeriesId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RecurringJobChange>().WithMany()
            .HasForeignKey(revision => revision.ChangeRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HouseholdMember>().WithMany()
            .HasForeignKey(revision => revision.CreatedByAdultId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
