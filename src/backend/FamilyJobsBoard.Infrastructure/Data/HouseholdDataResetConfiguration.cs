using FamilyJobsBoard.Domain.Administration;
using FamilyJobsBoard.Domain.Households;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class HouseholdDataResetConfiguration : IEntityTypeConfiguration<HouseholdDataReset>
{
    public void Configure(EntityTypeBuilder<HouseholdDataReset> builder)
    {
        builder.ToTable("household_data_resets");
        builder.HasKey(reset => reset.Id);
        builder.Property(reset => reset.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(reset => reset.InitiatedByAdultId).HasColumnName("initiated_by_adult_id");
        builder.Property(reset => reset.OccurredAtUtc).HasColumnName("occurred_at_utc");
        builder.Property(reset => reset.DeletedJobCount).HasColumnName("deleted_job_count");
        builder.Property(reset => reset.DeletedRecurringSeriesCount)
            .HasColumnName("deleted_recurring_series_count");
        builder.Property(reset => reset.DeletedReviewDecisionCount)
            .HasColumnName("deleted_review_decision_count");
        builder.Property(reset => reset.DeletedPointsEntryCount)
            .HasColumnName("deleted_points_entry_count");
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(reset => reset.InitiatedByAdultId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(reset => reset.OccurredAtUtc);
    }
}
