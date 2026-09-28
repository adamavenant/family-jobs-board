using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScopedRecurringJobChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "recurring_job_series",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "original_scheduled_date",
                table: "jobs",
                type: "date",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE jobs
                SET original_scheduled_date = scheduled_date
                WHERE recurring_job_series_id IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "recurring_job_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    anchor_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    operation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    updated_count = table.Column<int>(type: "integer", nullable: false),
                    created_count = table.Column<int>(type: "integer", nullable: false),
                    cancelled_count = table.Column<int>(type: "integer", nullable: false),
                    approved_skipped_count = table.Column<int>(type: "integer", nullable: false),
                    cancelled_skipped_count = table.Column<int>(type: "integer", nullable: false),
                    retrospective_point_increase_skipped_count = table.Column<int>(type: "integer", nullable: false),
                    warnings = table.Column<string[]>(type: "text[]", nullable: false),
                    series_version = table.Column<int>(type: "integer", nullable: false),
                    applied_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_job_changes", x => x.id);
                    table.ForeignKey(
                        name: "FK_recurring_job_changes_household_members_actor_member_id",
                        column: x => x.actor_member_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recurring_job_changes_jobs_anchor_job_id",
                        column: x => x.anchor_job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recurring_job_changes_recurring_job_series_series_id",
                        column: x => x.series_id,
                        principalTable: "recurring_job_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recurring_job_series_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_adult_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    agenda_period = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scheduled_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    weekday_mask = table.Column<int>(type: "integer", nullable: false),
                    monthly_day = table.Column<int>(type: "integer", nullable: true),
                    ended = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_job_series_revisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_recurring_job_series_revisions_household_members_created_by~",
                        column: x => x.created_by_adult_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recurring_job_series_revisions_recurring_job_changes_change~",
                        column: x => x.change_request_id,
                        principalTable: "recurring_job_changes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recurring_job_series_revisions_recurring_job_series_series_~",
                        column: x => x.series_id,
                        principalTable: "recurring_job_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_recurring_job_changes_actor_member_id",
                table: "recurring_job_changes",
                column: "actor_member_id");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_job_changes_anchor_job_id",
                table: "recurring_job_changes",
                column: "anchor_job_id");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_job_changes_series_id",
                table: "recurring_job_changes",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_job_series_revisions_change_request_id",
                table: "recurring_job_series_revisions",
                column: "change_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recurring_job_series_revisions_created_by_adult_id",
                table: "recurring_job_series_revisions",
                column: "created_by_adult_id");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_job_series_revisions_series_id_effective_from",
                table: "recurring_job_series_revisions",
                columns: new[] { "series_id", "effective_from" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recurring_job_series_revisions");

            migrationBuilder.DropTable(
                name: "recurring_job_changes");

            migrationBuilder.DropColumn(
                name: "version",
                table: "recurring_job_series");

            migrationBuilder.DropColumn(
                name: "original_scheduled_date",
                table: "jobs");
        }
    }
}
