using FamilyJobsBoard.Domain.GoodBehaviours;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Domain.PointAdjustments;
using FamilyJobsBoard.Domain.PointRedemptions;
using FamilyJobsBoard.Domain.Points;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class PointsLedgerEntryConfiguration : IEntityTypeConfiguration<PointsLedgerEntry>
{
    public const string JobApprovalIndexName = "ux_points_ledger_entries_job_approval";
    public const string ReversedEntryIndexName = "ux_points_ledger_entries_reversed_entry_id";

    public void Configure(EntityTypeBuilder<PointsLedgerEntry> builder)
    {
        builder.ToTable(
            "points_ledger_entries",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_points_ledger_entries_single_source",
                    "num_nonnulls(job_id, good_behaviour_id, point_adjustment_id, point_redemption_id) = 1");
                table.HasCheckConstraint(
                    "ck_points_ledger_entries_amount_sign",
                    "(point_adjustment_id IS NULL AND point_redemption_id IS NULL AND reversed_entry_id IS NULL AND amount >= 0) OR (point_adjustment_id IS NOT NULL AND amount <> 0) OR (point_redemption_id IS NOT NULL AND amount < 0) OR (reversed_entry_id IS NOT NULL AND amount <= 0)");
                // Job entries are either an approval's award, numbered by approval, or the
                // reversal of an award; no other entry has either column.
                table.HasCheckConstraint(
                    "ck_points_ledger_entries_job_entry",
                    "(job_id IS NULL AND job_approval_number IS NULL AND reversed_entry_id IS NULL) OR (job_id IS NOT NULL AND job_approval_number IS NOT NULL AND job_approval_number >= 1 AND reversed_entry_id IS NULL) OR (job_id IS NOT NULL AND job_approval_number IS NULL AND reversed_entry_id IS NOT NULL)");
            });
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).HasColumnName("id");
        builder.Property(entry => entry.ChildId).HasColumnName("child_id");
        builder.Property(entry => entry.JobId).HasColumnName("job_id");
        builder.Property(entry => entry.JobApprovalNumber).HasColumnName("job_approval_number");
        builder.Property(entry => entry.ReversedEntryId).HasColumnName("reversed_entry_id");
        builder.Property(entry => entry.GoodBehaviourId).HasColumnName("good_behaviour_id");
        builder.Property(entry => entry.PointAdjustmentId).HasColumnName("point_adjustment_id");
        builder.Property(entry => entry.PointRedemptionId).HasColumnName("point_redemption_id");
        builder.Property(entry => entry.Amount).HasColumnName("amount");
        builder.Property(entry => entry.AwardedAtUtc).HasColumnName("awarded_at_utc");
        // Serves the newest-first ledger, keyset paging, and per-child balances.
        builder.HasIndex(entry => new { entry.ChildId, entry.AwardedAtUtc, entry.Id })
            .HasDatabaseName("ix_points_ledger_entries_child_timeline");
        // Each approval of a job awards once. Reversals have no approval number, so they are
        // left out, and a job approved again after a reversal gets the next number.
        builder.HasIndex(entry => new { entry.JobId, entry.JobApprovalNumber })
            .IsUnique()
            .HasDatabaseName(JobApprovalIndexName);
        // Each award is reversed at most once.
        builder.HasIndex(entry => entry.ReversedEntryId)
            .IsUnique()
            .HasDatabaseName(ReversedEntryIndexName);
        builder.HasIndex(entry => entry.GoodBehaviourId)
            .IsUnique()
            .HasDatabaseName("ux_points_ledger_entries_good_behaviour_id");
        builder.HasIndex(entry => entry.PointAdjustmentId)
            .IsUnique()
            .HasDatabaseName("ux_points_ledger_entries_point_adjustment_id");
        builder.HasIndex(entry => entry.PointRedemptionId)
            .IsUnique()
            .HasDatabaseName("ux_points_ledger_entries_point_redemption_id");
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(entry => entry.ChildId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Job>()
            .WithMany()
            .HasForeignKey(entry => entry.JobId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PointsLedgerEntry>()
            .WithMany()
            .HasForeignKey(entry => entry.ReversedEntryId)
            .HasConstraintName("FK_points_ledger_entries_reversed_entry_id")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GoodBehaviour>()
            .WithMany()
            .HasForeignKey(entry => entry.GoodBehaviourId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PointAdjustment>()
            .WithMany()
            .HasForeignKey(entry => entry.PointAdjustmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PointRedemption>()
            .WithMany()
            .HasForeignKey(entry => entry.PointRedemptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
