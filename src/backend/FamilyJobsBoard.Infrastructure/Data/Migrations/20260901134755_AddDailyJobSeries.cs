using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyJobSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "agenda_period",
                table: "jobs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unscheduled");

            migrationBuilder.AddColumn<Guid>(
                name: "recurring_job_series_id",
                table: "jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "scheduled_time",
                table: "jobs",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "daily_job_series",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_adult_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    agenda_period = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scheduled_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    generated_through = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_job_series", x => x.id);
                    table.ForeignKey(
                        name: "FK_daily_job_series_household_members_child_id",
                        column: x => x.child_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_daily_job_series_household_members_created_by_adult_id",
                        column: x => x.created_by_adult_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_jobs_recurring_series_date",
                table: "jobs",
                columns: new[] { "recurring_job_series_id", "scheduled_date" },
                unique: true,
                filter: "recurring_job_series_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_daily_job_series_child_id_start_date",
                table: "daily_job_series",
                columns: new[] { "child_id", "start_date" });

            migrationBuilder.CreateIndex(
                name: "IX_daily_job_series_created_by_adult_id",
                table: "daily_job_series",
                column: "created_by_adult_id");

            migrationBuilder.AddForeignKey(
                name: "FK_jobs_daily_job_series_recurring_job_series_id",
                table: "jobs",
                column: "recurring_job_series_id",
                principalTable: "daily_job_series",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_jobs_daily_job_series_recurring_job_series_id",
                table: "jobs");

            migrationBuilder.DropTable(
                name: "daily_job_series");

            migrationBuilder.DropIndex(
                name: "ux_jobs_recurring_series_date",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "agenda_period",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "recurring_job_series_id",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "scheduled_time",
                table: "jobs");
        }
    }
}
