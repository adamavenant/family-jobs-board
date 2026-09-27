using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.TurnRotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class TurnRotationRevisionConfiguration : IEntityTypeConfiguration<TurnRotationRevision>
{
    public void Configure(EntityTypeBuilder<TurnRotationRevision> builder)
    {
        builder.ToTable("turn_rotation_revisions");
        builder.HasKey(revision => revision.Id);
        builder.Property(revision => revision.Id).HasColumnName("id");
        builder.Property(revision => revision.RotationId).HasColumnName("rotation_id");
        builder.Property(revision => revision.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(revision => revision.Question)
            .HasColumnName("question")
            .HasMaxLength(TurnRotationRevision.MaximumQuestionLength);
        builder.Property(revision => revision.FirstChildId).HasColumnName("first_child_id");
        builder.Property(revision => revision.CreatedByMemberId).HasColumnName("created_by_member_id");
        builder.Property(revision => revision.CreatedAtUtc).HasColumnName("created_at_utc");

        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(revision => revision.CreatedByMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(revision => new { revision.RotationId, revision.EffectiveFrom })
            .HasDatabaseName("ix_turn_rotation_revisions_rotation_effective_from");

        builder.Metadata
            .FindNavigation(nameof(TurnRotationRevision.Participants))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.OwnsMany(revision => revision.Participants, participants =>
        {
            participants.ToTable("turn_rotation_participants");
            participants.WithOwner().HasForeignKey("turn_rotation_revision_id");
            participants.Property<Guid>("turn_rotation_revision_id")
                .HasColumnName("turn_rotation_revision_id");
            participants.HasKey("turn_rotation_revision_id", nameof(TurnRotationParticipant.OrderIndex));
            participants.Property(participant => participant.ChildId).HasColumnName("child_id");
            participants.Property(participant => participant.OrderIndex)
                .HasColumnName("order_index")
                .ValueGeneratedNever();
            participants.HasOne<HouseholdMember>()
                .WithMany()
                .HasForeignKey(participant => participant.ChildId)
                .OnDelete(DeleteBehavior.Restrict);
            participants.HasIndex("turn_rotation_revision_id", nameof(TurnRotationParticipant.ChildId))
                .IsUnique()
                .HasDatabaseName("ux_turn_rotation_participants_revision_child");
        });
    }
}
