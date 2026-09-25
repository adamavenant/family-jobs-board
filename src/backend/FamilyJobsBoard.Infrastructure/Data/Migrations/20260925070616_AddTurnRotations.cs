using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTurnRotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "turn_rotation_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    question = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    first_child_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_turn_rotation_revisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_turn_rotation_revisions_household_members_created_by_member~",
                        column: x => x.created_by_member_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "turn_rotation_participants",
                columns: table => new
                {
                    order_index = table.Column<int>(type: "integer", nullable: false),
                    turn_rotation_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_turn_rotation_participants", x => new { x.turn_rotation_revision_id, x.order_index });
                    table.ForeignKey(
                        name: "FK_turn_rotation_participants_household_members_child_id",
                        column: x => x.child_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_turn_rotation_participants_turn_rotation_revisions_turn_rot~",
                        column: x => x.turn_rotation_revision_id,
                        principalTable: "turn_rotation_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_turn_rotation_participants_child_id",
                table: "turn_rotation_participants",
                column: "child_id");

            migrationBuilder.CreateIndex(
                name: "ux_turn_rotation_participants_revision_child",
                table: "turn_rotation_participants",
                columns: new[] { "turn_rotation_revision_id", "child_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_turn_rotation_revisions_created_by_member_id",
                table: "turn_rotation_revisions",
                column: "created_by_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_turn_rotation_revisions_effective_from",
                table: "turn_rotation_revisions",
                column: "effective_from");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "turn_rotation_participants");

            migrationBuilder.DropTable(
                name: "turn_rotation_revisions");
        }
    }
}
