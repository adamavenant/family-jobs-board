using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class MemberCredentialConfiguration : IEntityTypeConfiguration<MemberCredential>
{
    public void Configure(EntityTypeBuilder<MemberCredential> builder)
    {
        builder.ToTable(
            "member_credentials",
            table => table.HasCheckConstraint(
                "ck_member_credentials_ready_hash",
                "(state = 'Ready' AND pin_hash IS NOT NULL) OR (state = 'NotSet' AND pin_hash IS NULL)"));
        builder.HasKey(credential => credential.MemberId);
        builder.Property(credential => credential.MemberId).HasColumnName("member_id").ValueGeneratedNever();
        builder.Property(credential => credential.State)
            .HasColumnName("state")
            .HasConversion<string>()
            .HasMaxLength(16);
        builder.Property(credential => credential.PinHash).HasColumnName("pin_hash").HasMaxLength(512);
        builder.Property(credential => credential.PinSetAtUtc).HasColumnName("pin_set_at_utc");
        builder.Property(credential => credential.FailedWindowStartedAtUtc).HasColumnName("failed_window_started_at_utc");
        builder.Property(credential => credential.FailedAttemptCount).HasColumnName("failed_attempt_count");
        builder.Property(credential => credential.LockedUntilUtc).HasColumnName("locked_until_utc");
        builder.HasOne<HouseholdMember>()
            .WithOne()
            .HasForeignKey<MemberCredential>(credential => credential.MemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
