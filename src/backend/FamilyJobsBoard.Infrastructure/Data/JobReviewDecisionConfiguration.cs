using FamilyJobsBoard.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class JobReviewDecisionConfiguration : IEntityTypeConfiguration<JobReviewDecision>
{
    public void Configure(EntityTypeBuilder<JobReviewDecision> builder)
    {
        builder.ToTable("job_review_decisions");
        builder.HasKey(decision => decision.Id);
        builder.Property(decision => decision.Id).HasColumnName("id");
        builder.Property(decision => decision.JobId).HasColumnName("job_id");
        builder.Property(decision => decision.Outcome)
            .HasColumnName("outcome")
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(decision => decision.Reason)
            .HasColumnName("reason")
            .HasMaxLength(JobReviewDecision.MaximumReasonLength);
        builder.Property(decision => decision.DecidedAtUtc).HasColumnName("decided_at_utc");
        builder.HasIndex(decision => new { decision.JobId, decision.DecidedAtUtc });
        builder.HasOne<Job>()
            .WithMany()
            .HasForeignKey(decision => decision.JobId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
