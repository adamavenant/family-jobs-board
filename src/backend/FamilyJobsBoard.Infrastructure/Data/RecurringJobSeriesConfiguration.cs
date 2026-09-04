using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class RecurringJobSeriesConfiguration : IEntityTypeConfiguration<RecurringJobSeries>
{
    public void Configure(EntityTypeBuilder<RecurringJobSeries> builder)
    {
        builder.ToTable(
            "recurring_job_series",
            table => table.HasCheckConstraint(
                "ck_recurring_job_series_schedule",
                "(frequency = 'Daily' AND weekday_mask = 0 AND monthly_day IS NULL) OR " +
                "(frequency = 'Weekly' AND weekday_mask BETWEEN 1 AND 127 AND monthly_day IS NULL) OR " +
                "(frequency = 'Monthly' AND weekday_mask = 0 AND monthly_day BETWEEN 1 AND 31)"));
        builder.HasKey(series => series.Id);
        builder.Property(series => series.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(series => series.ChildId).HasColumnName("child_id");
        builder.Property(series => series.CreatedByAdultId).HasColumnName("created_by_adult_id");
        builder.Property(series => series.Name)
            .HasColumnName("name")
            .HasMaxLength(Job.MaximumNameLength);
        builder.Property(series => series.Description)
            .HasColumnName("description")
            .HasMaxLength(Job.MaximumDescriptionLength);
        builder.Property(series => series.Points).HasColumnName("points");
        builder.Property(series => series.AgendaPeriod)
            .HasColumnName("agenda_period")
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(series => series.ScheduledTime).HasColumnName("scheduled_time");
        builder.Property(series => series.StartDate).HasColumnName("start_date");
        builder.Property(series => series.EndDate).HasColumnName("end_date");
        builder.Property(series => series.Frequency)
            .HasColumnName("frequency")
            .HasConversion<string>()
            .HasMaxLength(16);
        builder.Property(series => series.WeekdayMask).HasColumnName("weekday_mask");
        builder.Property(series => series.MonthlyDay).HasColumnName("monthly_day");
        builder.Property(series => series.GeneratedThrough).HasColumnName("generated_through");
        builder.HasIndex(series => new { series.ChildId, series.StartDate });
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(series => series.ChildId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(series => series.CreatedByAdultId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
