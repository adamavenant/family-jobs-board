using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.TurnRotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class TakenTurnConfiguration : IEntityTypeConfiguration<TakenTurn>
{
    public const string RequestIdIndexName = "ux_turn_rotation_taken_turns_request_id";
    public const string CountedTurnIndexName = "ux_turn_rotation_taken_turns_revision_turn";

    public void Configure(EntityTypeBuilder<TakenTurn> builder)
    {
        builder.ToTable(
            "turn_rotation_taken_turns",
            table => table.HasCheckConstraint(
                "ck_turn_rotation_taken_turns_undone",
                "(undone_at_utc IS NULL) = (undone_by_member_id IS NULL)"));
        builder.HasKey(turn => turn.Id);
        builder.Property(turn => turn.Id).HasColumnName("id");
        builder.Property(turn => turn.RequestId).HasColumnName("request_id");
        builder.Property(turn => turn.RotationId).HasColumnName("rotation_id");
        builder.Property(turn => turn.RevisionId).HasColumnName("turn_rotation_revision_id");
        builder.Property(turn => turn.TurnNumber).HasColumnName("turn_number");
        builder.Property(turn => turn.Date).HasColumnName("turn_date");
        builder.Property(turn => turn.ChildId).HasColumnName("child_id");
        builder.Property(turn => turn.RecordedByMemberId).HasColumnName("recorded_by_member_id");
        builder.Property(turn => turn.RecordedAtUtc).HasColumnName("recorded_at_utc");
        builder.Property(turn => turn.UndoneByMemberId).HasColumnName("undone_by_member_id");
        builder.Property(turn => turn.UndoneAtUtc).HasColumnName("undone_at_utc");

        builder.HasIndex(turn => new { turn.RevisionId, turn.Date })
            .HasDatabaseName("ix_turn_rotation_taken_turns_revision_date");
        builder.HasIndex(turn => turn.RequestId)
            .IsUnique()
            .HasDatabaseName(RequestIdIndexName);

        // Each of a revision's turns can be taken by only one tick that still counts.
        builder.HasIndex(turn => new { turn.RevisionId, turn.TurnNumber })
            .IsUnique()
            .HasFilter("undone_at_utc IS NULL")
            .HasDatabaseName(CountedTurnIndexName);

        builder.HasOne<TurnRotationRevision>()
            .WithMany()
            .HasForeignKey(turn => turn.RevisionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(turn => turn.ChildId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(turn => turn.RecordedByMemberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(turn => turn.UndoneByMemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
