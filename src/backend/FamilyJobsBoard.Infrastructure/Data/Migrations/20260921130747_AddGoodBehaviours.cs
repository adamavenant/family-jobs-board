using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGoodBehaviours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "job_id",
                table: "points_ledger_entries",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "good_behaviour_id",
                table: "points_ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "good_behaviour_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_by_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deactivated_by_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deactivated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_good_behaviour_types", x => x.id);
                    table.CheckConstraint("ck_good_behaviour_types_points", "points >= 0");
                    table.ForeignKey(
                        name: "FK_good_behaviour_types_household_members_created_by_member_id",
                        column: x => x.created_by_member_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_good_behaviour_types_household_members_deactivated_by_membe~",
                        column: x => x.deactivated_by_member_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_good_behaviour_types_household_members_updated_by_member_id",
                        column: x => x.updated_by_member_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "good_behaviours",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    child_id = table.Column<Guid>(type: "uuid", nullable: false),
                    logged_by_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    logged_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_good_behaviours", x => x.id);
                    table.CheckConstraint("ck_good_behaviours_points", "points >= 0");
                    table.ForeignKey(
                        name: "FK_good_behaviours_good_behaviour_types_type_id",
                        column: x => x.type_id,
                        principalTable: "good_behaviour_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_good_behaviours_household_members_child_id",
                        column: x => x.child_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_good_behaviours_household_members_logged_by_member_id",
                        column: x => x.logged_by_member_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_points_ledger_entries_good_behaviour_id",
                table: "points_ledger_entries",
                column: "good_behaviour_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_points_ledger_entries_single_source",
                table: "points_ledger_entries",
                sql: "num_nonnulls(job_id, good_behaviour_id) = 1");

            migrationBuilder.CreateIndex(
                name: "IX_good_behaviour_types_created_by_member_id",
                table: "good_behaviour_types",
                column: "created_by_member_id");

            migrationBuilder.CreateIndex(
                name: "IX_good_behaviour_types_deactivated_by_member_id",
                table: "good_behaviour_types",
                column: "deactivated_by_member_id");

            migrationBuilder.CreateIndex(
                name: "IX_good_behaviour_types_updated_by_member_id",
                table: "good_behaviour_types",
                column: "updated_by_member_id");

            migrationBuilder.CreateIndex(
                name: "IX_good_behaviours_child_id_logged_at_utc",
                table: "good_behaviours",
                columns: new[] { "child_id", "logged_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_good_behaviours_logged_by_member_id",
                table: "good_behaviours",
                column: "logged_by_member_id");

            migrationBuilder.CreateIndex(
                name: "IX_good_behaviours_type_id",
                table: "good_behaviours",
                column: "type_id");

            migrationBuilder.CreateIndex(
                name: "ux_good_behaviours_request_id",
                table: "good_behaviours",
                column: "request_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_points_ledger_entries_good_behaviours_good_behaviour_id",
                table: "points_ledger_entries",
                column: "good_behaviour_id",
                principalTable: "good_behaviours",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_points_ledger_entries_good_behaviours_good_behaviour_id",
                table: "points_ledger_entries");

            migrationBuilder.DropTable(
                name: "good_behaviours");

            migrationBuilder.DropTable(
                name: "good_behaviour_types");

            migrationBuilder.DropIndex(
                name: "ux_points_ledger_entries_good_behaviour_id",
                table: "points_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_points_ledger_entries_single_source",
                table: "points_ledger_entries");

            migrationBuilder.DropColumn(
                name: "good_behaviour_id",
                table: "points_ledger_entries");

            migrationBuilder.AlterColumn<Guid>(
                name: "job_id",
                table: "points_ledger_entries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
