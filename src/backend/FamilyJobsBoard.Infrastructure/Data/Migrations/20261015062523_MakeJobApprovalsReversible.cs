using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeJobApprovalsReversible : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_points_ledger_entries_job_id",
                table: "points_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_points_ledger_entries_amount_sign",
                table: "points_ledger_entries");

            migrationBuilder.AddColumn<int>(
                name: "job_approval_number",
                table: "points_ledger_entries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "job_name",
                table: "points_ledger_entries",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reversed_entry_id",
                table: "points_ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "approval_count",
                table: "jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "completed_by_adult",
                table: "jobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Approvals were final until now, so every existing job award belongs to its job's
            // first and only approval, and its job's name hasn't changed since it was approved.
            // An adult marking a job done approves it at the instant it is completed; approving
            // a child's submission always happens later.
            migrationBuilder.Sql(
                "UPDATE points_ledger_entries SET job_approval_number = 1, job_name = jobs.name " +
                "FROM jobs WHERE points_ledger_entries.job_id = jobs.id;");
            migrationBuilder.Sql(
                "UPDATE jobs SET approval_count = 1 WHERE status = 'Approved' " +
                "OR EXISTS (SELECT 1 FROM points_ledger_entries entry WHERE entry.job_id = jobs.id);");
            migrationBuilder.Sql(
                "UPDATE jobs SET completed_by_adult = TRUE " +
                "WHERE status = 'Approved' AND completed_at_utc = approved_at_utc;");

            migrationBuilder.CreateIndex(
                name: "ux_points_ledger_entries_job_approval",
                table: "points_ledger_entries",
                columns: new[] { "job_id", "job_approval_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_points_ledger_entries_reversed_entry_id",
                table: "points_ledger_entries",
                column: "reversed_entry_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_points_ledger_entries_amount_sign",
                table: "points_ledger_entries",
                sql: "(point_adjustment_id IS NULL AND point_redemption_id IS NULL AND reversed_entry_id IS NULL AND amount >= 0) OR (point_adjustment_id IS NOT NULL AND amount <> 0) OR (point_redemption_id IS NOT NULL AND amount < 0) OR (reversed_entry_id IS NOT NULL AND amount <= 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_points_ledger_entries_job_entry",
                table: "points_ledger_entries",
                sql: "(job_id IS NULL AND job_approval_number IS NULL AND reversed_entry_id IS NULL) OR (job_id IS NOT NULL AND job_approval_number IS NOT NULL AND job_approval_number >= 1 AND reversed_entry_id IS NULL) OR (job_id IS NOT NULL AND job_approval_number IS NULL AND reversed_entry_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_points_ledger_entries_job_name",
                table: "points_ledger_entries",
                sql: "(job_id IS NULL) = (job_name IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_approval_tracking",
                table: "jobs",
                sql: "approval_count >= 0 AND (status <> 'Approved' OR approval_count >= 1) AND (NOT completed_by_adult OR status = 'Approved')");

            migrationBuilder.AddForeignKey(
                name: "FK_points_ledger_entries_reversed_entry_id",
                table: "points_ledger_entries",
                column: "reversed_entry_id",
                principalTable: "points_ledger_entries",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DO $$ BEGIN IF EXISTS " +
                "(SELECT 1 FROM points_ledger_entries WHERE reversed_entry_id IS NOT NULL) " +
                "THEN RAISE EXCEPTION 'Cannot downgrade while reversed job approvals exist.'; " +
                "END IF; END $$;");

            migrationBuilder.DropForeignKey(
                name: "FK_points_ledger_entries_reversed_entry_id",
                table: "points_ledger_entries");

            migrationBuilder.DropIndex(
                name: "ux_points_ledger_entries_job_approval",
                table: "points_ledger_entries");

            migrationBuilder.DropIndex(
                name: "ux_points_ledger_entries_reversed_entry_id",
                table: "points_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_points_ledger_entries_amount_sign",
                table: "points_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_points_ledger_entries_job_entry",
                table: "points_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_points_ledger_entries_job_name",
                table: "points_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_approval_tracking",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "job_approval_number",
                table: "points_ledger_entries");

            migrationBuilder.DropColumn(
                name: "job_name",
                table: "points_ledger_entries");

            migrationBuilder.DropColumn(
                name: "reversed_entry_id",
                table: "points_ledger_entries");

            migrationBuilder.DropColumn(
                name: "approval_count",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "completed_by_adult",
                table: "jobs");

            migrationBuilder.CreateIndex(
                name: "ux_points_ledger_entries_job_id",
                table: "points_ledger_entries",
                column: "job_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_points_ledger_entries_amount_sign",
                table: "points_ledger_entries",
                sql: "(point_adjustment_id IS NULL AND point_redemption_id IS NULL AND amount >= 0) OR (point_adjustment_id IS NOT NULL AND amount <> 0) OR (point_redemption_id IS NOT NULL AND amount < 0)");
        }
    }
}
