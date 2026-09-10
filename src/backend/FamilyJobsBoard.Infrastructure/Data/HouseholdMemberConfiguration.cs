using FamilyJobsBoard.Domain.Households;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class HouseholdMemberConfiguration : IEntityTypeConfiguration<HouseholdMember>
{
    public void Configure(EntityTypeBuilder<HouseholdMember> builder)
    {
        builder.ToTable(
            "household_members",
            table => table.HasCheckConstraint(
                "ck_household_members_role",
                "role IN ('Adult', 'Child')"));
        builder.HasKey(member => member.Id);
        builder.Property(member => member.Id).HasColumnName("id");
        builder.Ignore(member => member.DisplayName);
        builder.Property(member => member.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(HouseholdMember.MaximumNameLength);
        builder.Property(member => member.Nickname)
            .HasColumnName("nickname")
            .HasMaxLength(HouseholdMember.MaximumNameLength);
        builder.Property(member => member.Surname)
            .HasColumnName("surname")
            .HasMaxLength(HouseholdMember.MaximumNameLength);
        builder.Property(member => member.Role)
            .HasColumnName("role")
            .HasConversion<string>()
            .HasMaxLength(16);
        builder.Property(member => member.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);
        builder.Property(member => member.ProfileUpdatedAtUtc)
            .HasColumnName("profile_updated_at_utc");
        builder.Property(member => member.ProfileUpdatedByMemberId)
            .HasColumnName("profile_updated_by_member_id");
        builder.Property(member => member.DeactivatedAtUtc)
            .HasColumnName("deactivated_at_utc");
        builder.Property(member => member.DeactivatedByMemberId)
            .HasColumnName("deactivated_by_member_id");
        builder.Property(member => member.RestoredAtUtc)
            .HasColumnName("restored_at_utc");
        builder.Property(member => member.RestoredByMemberId)
            .HasColumnName("restored_by_member_id");
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(member => member.ProfileUpdatedByMemberId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(member => member.DeactivatedByMemberId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(member => member.RestoredByMemberId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.Ignore(member => member.IsAdult);
    }
}
