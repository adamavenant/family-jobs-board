using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPointRedemptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_points_ledger_entries_amount_sign",
                table: "points_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_points_ledger_entries_single_source",
                table: "points_ledger_entries");

            migrationBuilder.AddColumn<Guid>(
                name: "point_redemption_id",
                table: "points_ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "point_redemptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_id = table.Column<Guid>(type: "uuid", nullable: false),
                    redeemed_by_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    reward = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    redeemed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_point_redemptions", x => x.id);
                    table.CheckConstraint("ck_point_redemptions_points", "points > 0");
                    table.CheckConstraint("ck_point_redemptions_reward", "length(btrim(reward)) > 0");
                    table.ForeignKey(
                        name: "FK_point_redemptions_household_members_child_id",
                        column: x => x.child_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_point_redemptions_household_members_redeemed_by_member_id",
                        column: x => x.redeemed_by_member_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_points_ledger_entries_point_redemption_id",
                table: "points_ledger_entries",
                column: "point_redemption_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_points_ledger_entries_amount_sign",
                table: "points_ledger_entries",
                sql: "(point_adjustment_id IS NULL AND point_redemption_id IS NULL AND amount >= 0) OR (point_adjustment_id IS NOT NULL AND amount <> 0) OR (point_redemption_id IS NOT NULL AND amount < 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_points_ledger_entries_single_source",
                table: "points_ledger_entries",
                sql: "num_nonnulls(job_id, good_behaviour_id, point_adjustment_id, point_redemption_id) = 1");

            migrationBuilder.CreateIndex(
                name: "IX_point_redemptions_child_id_redeemed_at_utc",
                table: "point_redemptions",
                columns: new[] { "child_id", "redeemed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_point_redemptions_redeemed_by_member_id",
                table: "point_redemptions",
                column: "redeemed_by_member_id");

            migrationBuilder.CreateIndex(
                name: "ux_point_redemptions_request_id",
                table: "point_redemptions",
                column: "request_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_points_ledger_entries_point_redemptions_point_redemption_id",
                table: "points_ledger_entries",
                column: "point_redemption_id",
                principalTable: "point_redemptions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_points_ledger_entries_point_redemptions_point_redemption_id",
                table: "points_ledger_entries");

            migrationBuilder.DropTable(
                name: "point_redemptions");

            migrationBuilder.DropIndex(
                name: "ux_points_ledger_entries_point_redemption_id",
                table: "points_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_points_ledger_entries_amount_sign",
                table: "points_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_points_ledger_entries_single_source",
                table: "points_ledger_entries");

            migrationBuilder.DropColumn(
                name: "point_redemption_id",
                table: "points_ledger_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_points_ledger_entries_amount_sign",
                table: "points_ledger_entries",
                sql: "(point_adjustment_id IS NULL AND amount >= 0) OR (point_adjustment_id IS NOT NULL AND amount <> 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_points_ledger_entries_single_source",
                table: "points_ledger_entries",
                sql: "num_nonnulls(job_id, good_behaviour_id, point_adjustment_id) = 1");
        }
    }
}
