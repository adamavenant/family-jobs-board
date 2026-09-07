using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class HouseholdBootstrapConfiguration : IEntityTypeConfiguration<HouseholdBootstrap>
{
    public void Configure(EntityTypeBuilder<HouseholdBootstrap> builder)
    {
        builder.ToTable(
            "household_bootstrap",
            table => table.HasCheckConstraint("ck_household_bootstrap_singleton", "id = 1"));
        builder.HasKey(bootstrap => bootstrap.Id);
        builder.Property(bootstrap => bootstrap.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(bootstrap => bootstrap.State)
            .HasColumnName("state")
            .HasConversion<string>()
            .HasMaxLength(16);
        builder.Property(bootstrap => bootstrap.FirstAdultId).HasColumnName("first_adult_id");
        builder.Property(bootstrap => bootstrap.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(bootstrap => bootstrap.FirstAdultId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
