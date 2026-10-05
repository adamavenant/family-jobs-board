using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.PointRedemptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class PointRedemptionConfiguration : IEntityTypeConfiguration<PointRedemption>
{
    public const string RequestIdIndexName = "ux_point_redemptions_request_id";

    public void Configure(EntityTypeBuilder<PointRedemption> builder)
    {
        builder.ToTable(
            "point_redemptions",
            table =>
            {
                table.HasCheckConstraint("ck_point_redemptions_points", "points > 0");
                table.HasCheckConstraint("ck_point_redemptions_reward", "length(btrim(reward)) > 0");
            });
        builder.HasKey(redemption => redemption.Id);
        builder.Property(redemption => redemption.Id).HasColumnName("id");
        builder.Property(redemption => redemption.RequestId).HasColumnName("request_id");
        builder.Property(redemption => redemption.ChildId).HasColumnName("child_id");
        builder.Property(redemption => redemption.RedeemedByMemberId)
            .HasColumnName("redeemed_by_member_id");
        builder.Property(redemption => redemption.Points).HasColumnName("points");
        builder.Property(redemption => redemption.Reward)
            .HasColumnName("reward")
            .HasMaxLength(PointRedemption.MaximumRewardLength);
        builder.Property(redemption => redemption.RedeemedAtUtc).HasColumnName("redeemed_at_utc");
        builder.HasIndex(redemption => redemption.RequestId)
            .IsUnique()
            .HasDatabaseName(RequestIdIndexName);
        builder.HasIndex(redemption => new { redemption.ChildId, redemption.RedeemedAtUtc });
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(redemption => redemption.ChildId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(redemption => redemption.RedeemedByMemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
