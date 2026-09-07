using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class PinSetupTokenConfiguration : IEntityTypeConfiguration<PinSetupToken>
{
    public void Configure(EntityTypeBuilder<PinSetupToken> builder)
    {
        builder.ToTable("pin_setup_tokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(token => token.TargetMemberId).HasColumnName("target_member_id");
        builder.Property(token => token.AuthorizingAdultId).HasColumnName("authorizing_adult_id");
        builder.Property(token => token.TokenHash).HasColumnName("token_hash").HasMaxLength(64);
        builder.Property(token => token.ExpiresAtUtc).HasColumnName("expires_at_utc");
        builder.Property(token => token.ConsumedAtUtc).HasColumnName("consumed_at_utc");
        builder.Property(token => token.RevokedAtUtc).HasColumnName("revoked_at_utc");
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(token => token.TargetMemberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(token => token.AuthorizingAdultId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(token => token.TargetMemberId);
    }
}
