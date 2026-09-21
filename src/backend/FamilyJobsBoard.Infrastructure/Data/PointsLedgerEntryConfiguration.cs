using FamilyJobsBoard.Domain.GoodBehaviours;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Domain.PointAdjustments;
using FamilyJobsBoard.Domain.Points;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class PointsLedgerEntryConfiguration : IEntityTypeConfiguration<PointsLedgerEntry>
{
    public void Configure(EntityTypeBuilder<PointsLedgerEntry> builder)
    {
        builder.ToTable(
            "points_ledger_entries",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_points_ledger_entries_single_source",
                    "num_nonnulls(job_id, good_behaviour_id, point_adjustment_id) = 1");
                table.HasCheckConstraint(
                    "ck_points_ledger_entries_amount_sign",
                    "(point_adjustment_id IS NULL AND amount >= 0) OR (point_adjustment_id IS NOT NULL AND amount <> 0)");
            });
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).HasColumnName("id");
        builder.Property(entry => entry.ChildId).HasColumnName("child_id");
        builder.Property(entry => entry.JobId).HasColumnName("job_id");
        builder.Property(entry => entry.GoodBehaviourId).HasColumnName("good_behaviour_id");
        builder.Property(entry => entry.PointAdjustmentId).HasColumnName("point_adjustment_id");
        builder.Property(entry => entry.Amount).HasColumnName("amount");
        builder.Property(entry => entry.AwardedAtUtc).HasColumnName("awarded_at_utc");
        builder.HasIndex(entry => entry.JobId)
            .IsUnique()
            .HasDatabaseName("ux_points_ledger_entries_job_id");
        builder.HasIndex(entry => entry.GoodBehaviourId)
            .IsUnique()
            .HasDatabaseName("ux_points_ledger_entries_good_behaviour_id");
        builder.HasIndex(entry => entry.PointAdjustmentId)
            .IsUnique()
            .HasDatabaseName("ux_points_ledger_entries_point_adjustment_id");
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(entry => entry.ChildId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Job>()
            .WithMany()
            .HasForeignKey(entry => entry.JobId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GoodBehaviour>()
            .WithMany()
            .HasForeignKey(entry => entry.GoodBehaviourId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PointAdjustment>()
            .WithMany()
            .HasForeignKey(entry => entry.PointAdjustmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
