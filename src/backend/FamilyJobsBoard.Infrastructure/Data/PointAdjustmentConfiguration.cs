using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.PointAdjustments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class PointAdjustmentConfiguration : IEntityTypeConfiguration<PointAdjustment>
{
    public const string RequestIdIndexName = "ux_point_adjustments_request_id";

    public void Configure(EntityTypeBuilder<PointAdjustment> builder)
    {
        builder.ToTable(
            "point_adjustments",
            table =>
            {
                table.HasCheckConstraint("ck_point_adjustments_amount", "amount <> 0");
                table.HasCheckConstraint("ck_point_adjustments_reason", "length(btrim(reason)) > 0");
            });
        builder.HasKey(adjustment => adjustment.Id);
        builder.Property(adjustment => adjustment.Id).HasColumnName("id");
        builder.Property(adjustment => adjustment.RequestId).HasColumnName("request_id");
        builder.Property(adjustment => adjustment.ChildId).HasColumnName("child_id");
        builder.Property(adjustment => adjustment.AdjustedByMemberId)
            .HasColumnName("adjusted_by_member_id");
        builder.Property(adjustment => adjustment.Amount).HasColumnName("amount");
        builder.Property(adjustment => adjustment.Reason)
            .HasColumnName("reason")
            .HasMaxLength(PointAdjustment.MaximumReasonLength);
        builder.Property(adjustment => adjustment.AdjustedAtUtc).HasColumnName("adjusted_at_utc");
        builder.HasIndex(adjustment => adjustment.RequestId)
            .IsUnique()
            .HasDatabaseName(RequestIdIndexName);
        builder.HasIndex(adjustment => new { adjustment.ChildId, adjustment.AdjustedAtUtc });
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(adjustment => adjustment.ChildId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(adjustment => adjustment.AdjustedByMemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
