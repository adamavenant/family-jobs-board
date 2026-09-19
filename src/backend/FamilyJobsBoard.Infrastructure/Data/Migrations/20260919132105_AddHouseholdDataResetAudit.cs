using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseholdDataResetAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "household_data_resets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    initiated_by_adult_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_job_count = table.Column<int>(type: "integer", nullable: false),
                    deleted_recurring_series_count = table.Column<int>(type: "integer", nullable: false),
                    deleted_review_decision_count = table.Column<int>(type: "integer", nullable: false),
                    deleted_points_entry_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_household_data_resets", x => x.id);
                    table.ForeignKey(
                        name: "FK_household_data_resets_household_members_initiated_by_adult_~",
                        column: x => x.initiated_by_adult_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_household_data_resets_initiated_by_adult_id",
                table: "household_data_resets",
                column: "initiated_by_adult_id");

            migrationBuilder.CreateIndex(
                name: "IX_household_data_resets_occurred_at_utc",
                table: "household_data_resets",
                column: "occurred_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "household_data_resets");
        }
    }
}
