using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class RecurringJobChangeConfiguration : IEntityTypeConfiguration<RecurringJobChange>
{
    public void Configure(EntityTypeBuilder<RecurringJobChange> builder)
    {
        builder.ToTable("recurring_job_changes");
        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(change => change.AnchorJobId).HasColumnName("anchor_job_id");
        builder.Property(change => change.SeriesId).HasColumnName("series_id");
        builder.Property(change => change.ActorMemberId).HasColumnName("actor_member_id");
        builder.Property(change => change.Fingerprint)
            .HasColumnName("fingerprint")
            .HasMaxLength(64);
        builder.Property(change => change.Operation).HasColumnName("operation").HasMaxLength(16);
        builder.Property(change => change.Scope).HasColumnName("scope").HasMaxLength(16);
        builder.Property(change => change.UpdatedCount).HasColumnName("updated_count");
        builder.Property(change => change.CreatedCount).HasColumnName("created_count");
        builder.Property(change => change.CancelledCount).HasColumnName("cancelled_count");
        builder.Property(change => change.ApprovedSkippedCount)
            .HasColumnName("approved_skipped_count");
        builder.Property(change => change.CancelledSkippedCount)
            .HasColumnName("cancelled_skipped_count");
        builder.Property(change => change.RetrospectivePointIncreaseSkippedCount)
            .HasColumnName("retrospective_point_increase_skipped_count");
        builder.Property(change => change.Warnings).HasColumnName("warnings");
        builder.Property(change => change.SeriesVersion).HasColumnName("series_version");
        builder.Property(change => change.AppliedAtUtc).HasColumnName("applied_at_utc");
        builder.HasOne<Job>().WithMany().HasForeignKey(change => change.AnchorJobId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RecurringJobSeries>().WithMany().HasForeignKey(change => change.SeriesId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HouseholdMember>().WithMany().HasForeignKey(change => change.ActorMemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
