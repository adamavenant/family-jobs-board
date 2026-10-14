using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class HouseholdSettingsConfiguration : IEntityTypeConfiguration<HouseholdSettings>
{
    public const string PrimaryKeyName = "pk_household_settings";

    public void Configure(EntityTypeBuilder<HouseholdSettings> builder)
    {
        builder.ToTable(
            "household_settings",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_household_settings_singleton",
                    $"id = {HouseholdSettings.SingletonId}");
                table.HasCheckConstraint(
                    "ck_household_settings_undo_grace_period_seconds",
                    $"undo_grace_period_seconds BETWEEN {HouseholdSettings.MinimumUndoGracePeriodSeconds} AND {HouseholdSettings.MaximumUndoGracePeriodSeconds}");
            });
        builder.HasKey(settings => settings.Id).HasName(PrimaryKeyName);
        builder.Property(settings => settings.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(settings => settings.UndoGracePeriodSeconds)
            .HasColumnName("undo_grace_period_seconds");
        builder.Property(settings => settings.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property(settings => settings.UpdatedByMemberId)
            .HasColumnName("updated_by_member_id");
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(settings => settings.UpdatedByMemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
