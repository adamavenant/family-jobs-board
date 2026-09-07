using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("auth_sessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(session => session.MemberId).HasColumnName("member_id");
        builder.Property(session => session.Role)
            .HasColumnName("role")
            .HasConversion<string>()
            .HasMaxLength(16);
        builder.Property(session => session.RefreshTokenHash)
            .HasColumnName("refresh_token_hash")
            .HasMaxLength(64);
        builder.Property(session => session.RotationVersion).HasColumnName("rotation_version");
        builder.Property(session => session.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(session => session.LastActivityAtUtc).HasColumnName("last_activity_at_utc");
        builder.Property(session => session.RevokedAtUtc).HasColumnName("revoked_at_utc");
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(session => session.MemberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(session => session.MemberId);
    }
}
