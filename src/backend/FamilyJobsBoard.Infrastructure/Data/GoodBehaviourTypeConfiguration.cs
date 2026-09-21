using FamilyJobsBoard.Domain.GoodBehaviours;
using FamilyJobsBoard.Domain.Households;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class GoodBehaviourTypeConfiguration : IEntityTypeConfiguration<GoodBehaviourType>
{
    public void Configure(EntityTypeBuilder<GoodBehaviourType> builder)
    {
        builder.ToTable(
            "good_behaviour_types",
            table => table.HasCheckConstraint("ck_good_behaviour_types_points", "points >= 0"));
        builder.HasKey(type => type.Id);
        builder.Property(type => type.Id).HasColumnName("id");
        builder.Property(type => type.Name)
            .HasColumnName("name")
            .HasMaxLength(GoodBehaviourType.MaximumNameLength);
        builder.Property(type => type.Description)
            .HasColumnName("description")
            .HasMaxLength(GoodBehaviourType.MaximumDescriptionLength);
        builder.Property(type => type.Points).HasColumnName("points");
        builder.Property(type => type.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);
        builder.Property(type => type.CreatedByMemberId).HasColumnName("created_by_member_id");
        builder.Property(type => type.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(type => type.UpdatedByMemberId).HasColumnName("updated_by_member_id");
        builder.Property(type => type.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property(type => type.DeactivatedByMemberId)
            .HasColumnName("deactivated_by_member_id");
        builder.Property(type => type.DeactivatedAtUtc).HasColumnName("deactivated_at_utc");
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(type => type.CreatedByMemberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(type => type.UpdatedByMemberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(type => type.DeactivatedByMemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
