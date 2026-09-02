using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklyRecurringJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_jobs_daily_job_series_recurring_job_series_id",
                table: "jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_daily_job_series_household_members_child_id",
                table: "daily_job_series");

            migrationBuilder.DropForeignKey(
                name: "FK_daily_job_series_household_members_created_by_adult_id",
                table: "daily_job_series");

            migrationBuilder.DropPrimaryKey(
                name: "PK_daily_job_series",
                table: "daily_job_series");

            migrationBuilder.RenameTable(
                name: "daily_job_series",
                newName: "recurring_job_series");

            migrationBuilder.RenameIndex(
                name: "IX_daily_job_series_created_by_adult_id",
                table: "recurring_job_series",
                newName: "IX_recurring_job_series_created_by_adult_id");

            migrationBuilder.RenameIndex(
                name: "IX_daily_job_series_child_id_start_date",
                table: "recurring_job_series",
                newName: "IX_recurring_job_series_child_id_start_date");

            migrationBuilder.AddColumn<string>(
                name: "recurrence_frequency",
                table: "jobs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "frequency",
                table: "recurring_job_series",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Daily");

            migrationBuilder.AddColumn<int>(
                name: "weekday_mask",
                table: "recurring_job_series",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                "UPDATE jobs SET recurrence_frequency = 'Daily' " +
                "WHERE recurring_job_series_id IS NOT NULL;");

            migrationBuilder.AddPrimaryKey(
                name: "PK_recurring_job_series",
                table: "recurring_job_series",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_recurring_job_series_household_members_child_id",
                table: "recurring_job_series",
                column: "child_id",
                principalTable: "household_members",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_recurring_job_series_household_members_created_by_adult_id",
                table: "recurring_job_series",
                column: "created_by_adult_id",
                principalTable: "household_members",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_jobs_recurring_job_series_recurring_job_series_id",
                table: "jobs",
                column: "recurring_job_series_id",
                principalTable: "recurring_job_series",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_jobs_recurring_job_series_recurring_job_series_id",
                table: "jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_recurring_job_series_household_members_child_id",
                table: "recurring_job_series");

            migrationBuilder.DropForeignKey(
                name: "FK_recurring_job_series_household_members_created_by_adult_id",
                table: "recurring_job_series");

            migrationBuilder.DropPrimaryKey(
                name: "PK_recurring_job_series",
                table: "recurring_job_series");

            migrationBuilder.Sql(
                "DO $$ BEGIN IF EXISTS " +
                "(SELECT 1 FROM recurring_job_series WHERE frequency = 'Weekly') " +
                "THEN RAISE EXCEPTION 'Cannot downgrade while weekly recurring jobs exist.'; " +
                "END IF; END $$;");

            migrationBuilder.DropColumn(
                name: "recurrence_frequency",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "frequency",
                table: "recurring_job_series");

            migrationBuilder.DropColumn(
                name: "weekday_mask",
                table: "recurring_job_series");

            migrationBuilder.RenameTable(
                name: "recurring_job_series",
                newName: "daily_job_series");

            migrationBuilder.RenameIndex(
                name: "IX_recurring_job_series_created_by_adult_id",
                table: "daily_job_series",
                newName: "IX_daily_job_series_created_by_adult_id");

            migrationBuilder.RenameIndex(
                name: "IX_recurring_job_series_child_id_start_date",
                table: "daily_job_series",
                newName: "IX_daily_job_series_child_id_start_date");

            migrationBuilder.AddPrimaryKey(
                name: "PK_daily_job_series",
                table: "daily_job_series",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_daily_job_series_household_members_child_id",
                table: "daily_job_series",
                column: "child_id",
                principalTable: "household_members",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_daily_job_series_household_members_created_by_adult_id",
                table: "daily_job_series",
                column: "created_by_adult_id",
                principalTable: "household_members",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_jobs_daily_job_series_recurring_job_series_id",
                table: "jobs",
                column: "recurring_job_series_id",
                principalTable: "daily_job_series",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
