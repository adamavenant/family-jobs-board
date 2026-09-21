using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPointAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_points_ledger_entries_single_source",
                table: "points_ledger_entries");

            migrationBuilder.AddColumn<Guid>(
                name: "point_adjustment_id",
                table: "points_ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "point_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adjusted_by_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    adjusted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_point_adjustments", x => x.id);
                    table.CheckConstraint("ck_point_adjustments_amount", "amount <> 0");
                    table.CheckConstraint("ck_point_adjustments_reason", "length(btrim(reason)) > 0");
                    table.ForeignKey(
                        name: "FK_point_adjustments_household_members_adjusted_by_member_id",
                        column: x => x.adjusted_by_member_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_point_adjustments_household_members_child_id",
                        column: x => x.child_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_points_ledger_entries_point_adjustment_id",
                table: "points_ledger_entries",
                column: "point_adjustment_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_points_ledger_entries_amount_sign",
                table: "points_ledger_entries",
                sql: "(point_adjustment_id IS NULL AND amount >= 0) OR (point_adjustment_id IS NOT NULL AND amount <> 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_points_ledger_entries_single_source",
                table: "points_ledger_entries",
                sql: "num_nonnulls(job_id, good_behaviour_id, point_adjustment_id) = 1");

            migrationBuilder.CreateIndex(
                name: "IX_point_adjustments_adjusted_by_member_id",
                table: "point_adjustments",
                column: "adjusted_by_member_id");

            migrationBuilder.CreateIndex(
                name: "IX_point_adjustments_child_id_adjusted_at_utc",
                table: "point_adjustments",
                columns: new[] { "child_id", "adjusted_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_point_adjustments_request_id",
                table: "point_adjustments",
                column: "request_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_points_ledger_entries_point_adjustments_point_adjustment_id",
                table: "points_ledger_entries",
                column: "point_adjustment_id",
                principalTable: "point_adjustments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_points_ledger_entries_point_adjustments_point_adjustment_id",
                table: "points_ledger_entries");

            migrationBuilder.DropTable(
                name: "point_adjustments");

            migrationBuilder.DropIndex(
                name: "ux_points_ledger_entries_point_adjustment_id",
                table: "points_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_points_ledger_entries_amount_sign",
                table: "points_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_points_ledger_entries_single_source",
                table: "points_ledger_entries");

            migrationBuilder.DropColumn(
                name: "point_adjustment_id",
                table: "points_ledger_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_points_ledger_entries_single_source",
                table: "points_ledger_entries",
                sql: "num_nonnulls(job_id, good_behaviour_id) = 1");
        }
    }
}
