using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPointsLedgerTimelineIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_points_ledger_entries_child_id",
                table: "points_ledger_entries");

            migrationBuilder.CreateIndex(
                name: "ix_points_ledger_entries_child_timeline",
                table: "points_ledger_entries",
                columns: new[] { "child_id", "awarded_at_utc", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_points_ledger_entries_child_timeline",
                table: "points_ledger_entries");

            migrationBuilder.CreateIndex(
                name: "IX_points_ledger_entries_child_id",
                table: "points_ledger_entries",
                column: "child_id");
        }
    }
}
