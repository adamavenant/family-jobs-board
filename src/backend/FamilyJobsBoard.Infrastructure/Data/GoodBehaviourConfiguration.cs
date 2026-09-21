using FamilyJobsBoard.Domain.GoodBehaviours;
using FamilyJobsBoard.Domain.Households;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyJobsBoard.Infrastructure.Data;

internal sealed class GoodBehaviourConfiguration : IEntityTypeConfiguration<GoodBehaviour>
{
    public const string RequestIdIndexName = "ux_good_behaviours_request_id";

    public void Configure(EntityTypeBuilder<GoodBehaviour> builder)
    {
        builder.ToTable(
            "good_behaviours",
            table => table.HasCheckConstraint("ck_good_behaviours_points", "points >= 0"));
        builder.HasKey(behaviour => behaviour.Id);
        builder.Property(behaviour => behaviour.Id).HasColumnName("id");
        builder.Property(behaviour => behaviour.RequestId).HasColumnName("request_id");
        builder.Property(behaviour => behaviour.TypeId).HasColumnName("type_id");
        builder.Property(behaviour => behaviour.TypeName)
            .HasColumnName("type_name")
            .HasMaxLength(GoodBehaviourType.MaximumNameLength);
        builder.Property(behaviour => behaviour.TypeDescription)
            .HasColumnName("type_description")
            .HasMaxLength(GoodBehaviourType.MaximumDescriptionLength);
        builder.Property(behaviour => behaviour.ChildId).HasColumnName("child_id");
        builder.Property(behaviour => behaviour.LoggedByMemberId)
            .HasColumnName("logged_by_member_id");
        builder.Property(behaviour => behaviour.Points).HasColumnName("points");
        builder.Property(behaviour => behaviour.LoggedAtUtc).HasColumnName("logged_at_utc");
        builder.HasIndex(behaviour => behaviour.RequestId)
            .IsUnique()
            .HasDatabaseName(RequestIdIndexName);
        builder.HasIndex(behaviour => new { behaviour.ChildId, behaviour.LoggedAtUtc });
        builder.HasOne<GoodBehaviourType>()
            .WithMany()
            .HasForeignKey(behaviour => behaviour.TypeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(behaviour => behaviour.ChildId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HouseholdMember>()
            .WithMany()
            .HasForeignKey(behaviour => behaviour.LoggedByMemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
