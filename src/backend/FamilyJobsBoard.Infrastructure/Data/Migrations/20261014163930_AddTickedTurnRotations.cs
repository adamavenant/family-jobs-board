using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTickedTurnRotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "advance_mode",
                table: "turn_rotation_revisions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Daily");

            migrationBuilder.CreateTable(
                name: "turn_rotation_taken_turns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rotation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    turn_rotation_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    turn_date = table.Column<DateOnly>(type: "date", nullable: false),
                    child_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_by_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    undone_by_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    undone_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_turn_rotation_taken_turns", x => x.id);
                    table.CheckConstraint("ck_turn_rotation_taken_turns_undone", "(undone_at_utc IS NULL) = (undone_by_member_id IS NULL)");
                    table.ForeignKey(
                        name: "FK_turn_rotation_taken_turns_household_members_child_id",
                        column: x => x.child_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_turn_rotation_taken_turns_household_members_recorded_by_mem~",
                        column: x => x.recorded_by_member_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_turn_rotation_taken_turns_household_members_undone_by_membe~",
                        column: x => x.undone_by_member_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_turn_rotation_taken_turns_turn_rotation_revisions_turn_rota~",
                        column: x => x.turn_rotation_revision_id,
                        principalTable: "turn_rotation_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_turn_rotation_taken_turns_child_id",
                table: "turn_rotation_taken_turns",
                column: "child_id");

            migrationBuilder.CreateIndex(
                name: "IX_turn_rotation_taken_turns_recorded_by_member_id",
                table: "turn_rotation_taken_turns",
                column: "recorded_by_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_turn_rotation_taken_turns_revision_date",
                table: "turn_rotation_taken_turns",
                columns: new[] { "turn_rotation_revision_id", "turn_date" });

            migrationBuilder.CreateIndex(
                name: "IX_turn_rotation_taken_turns_undone_by_member_id",
                table: "turn_rotation_taken_turns",
                column: "undone_by_member_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "turn_rotation_taken_turns");

            migrationBuilder.DropColumn(
                name: "advance_mode",
                table: "turn_rotation_revisions");
        }
    }
}
