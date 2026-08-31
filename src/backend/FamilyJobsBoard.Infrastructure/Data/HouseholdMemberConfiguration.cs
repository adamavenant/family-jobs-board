using FamilyJobsBoard.Domain.Households;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class HouseholdMemberConfiguration : IEntityTypeConfiguration<HouseholdMember>
{
    public void Configure(EntityTypeBuilder<HouseholdMember> builder)
    {
        builder.ToTable("household_members");
        builder.HasKey(member => member.Id);
        builder.Property(member => member.Id).HasColumnName("id");
        builder.Ignore(member => member.DisplayName);
        builder.Property(member => member.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(HouseholdMember.MaximumNameLength);
        builder.Property(member => member.Nickname)
            .HasColumnName("nickname")
            .HasMaxLength(HouseholdMember.MaximumNameLength);
        builder.Property(member => member.IsAdult).HasColumnName("is_adult");
    }
}
