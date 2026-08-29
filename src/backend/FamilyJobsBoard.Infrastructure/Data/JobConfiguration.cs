using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs");
        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).HasColumnName("id");
        builder.Property(job => job.ChildId).HasColumnName("child_id");
        builder.Property(job => job.Name).HasColumnName("name").HasMaxLength(160);
        builder.Property(job => job.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(job => job.Points).HasColumnName("points");
        builder.Property(job => job.ScheduledDate).HasColumnName("scheduled_date");
        builder.Property(job => job.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(job => job.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.HasIndex(job => new { job.ChildId, job.ScheduledDate });
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(job => job.ChildId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
