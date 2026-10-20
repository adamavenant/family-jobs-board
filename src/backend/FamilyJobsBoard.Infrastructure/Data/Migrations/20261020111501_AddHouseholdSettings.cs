using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseholdSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "household_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    undo_grace_period_seconds = table.Column<int>(type: "integer", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_member_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_household_settings", x => x.id);
                    table.CheckConstraint("ck_household_settings_singleton", "id = 1");
                    table.CheckConstraint("ck_household_settings_undo_grace_period_seconds", "undo_grace_period_seconds BETWEEN 5 AND 120");
                    table.ForeignKey(
                        name: "FK_household_settings_household_members_updated_by_member_id",
                        column: x => x.updated_by_member_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_household_settings_updated_by_member_id",
                table: "household_settings",
                column: "updated_by_member_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "household_settings");
        }
    }
}
