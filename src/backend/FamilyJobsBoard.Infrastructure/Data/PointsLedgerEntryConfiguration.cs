using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Domain.Points;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class PointsLedgerEntryConfiguration : IEntityTypeConfiguration<PointsLedgerEntry>
{
    public void Configure(EntityTypeBuilder<PointsLedgerEntry> builder)
    {
        builder.ToTable("points_ledger_entries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).HasColumnName("id");
        builder.Property(entry => entry.ChildId).HasColumnName("child_id");
        builder.Property(entry => entry.JobId).HasColumnName("job_id");
        builder.Property(entry => entry.Amount).HasColumnName("amount");
        builder.Property(entry => entry.AwardedAtUtc).HasColumnName("awarded_at_utc");
        builder.HasIndex(entry => entry.JobId)
            .IsUnique()
            .HasDatabaseName("ux_points_ledger_entries_job_id");
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(entry => entry.ChildId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Job>()
            .WithMany()
            .HasForeignKey(entry => entry.JobId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
