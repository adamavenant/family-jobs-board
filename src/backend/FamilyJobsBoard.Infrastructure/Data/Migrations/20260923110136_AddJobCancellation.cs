using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                table: "jobs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at_utc",
                table: "jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cancelled_by_member_id",
                table: "jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_jobs_cancelled_by_member_id",
                table: "jobs",
                column: "cancelled_by_member_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_cancellation_details",
                table: "jobs",
                sql: "(status = 'Cancelled' AND cancelled_by_member_id IS NOT NULL AND cancelled_at_utc IS NOT NULL) OR (status <> 'Cancelled' AND cancelled_by_member_id IS NULL AND cancelled_at_utc IS NULL AND cancellation_reason IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_jobs_household_members_cancelled_by_member_id",
                table: "jobs",
                column: "cancelled_by_member_id",
                principalTable: "household_members",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DO $$ BEGIN IF EXISTS " +
                "(SELECT 1 FROM jobs WHERE status = 'Cancelled') " +
                "THEN RAISE EXCEPTION 'Cannot downgrade while cancelled jobs exist.'; " +
                "END IF; END $$;");

            migrationBuilder.DropForeignKey(
                name: "FK_jobs_household_members_cancelled_by_member_id",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "IX_jobs_cancelled_by_member_id",
                table: "jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_cancellation_details",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "cancelled_at_utc",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "cancelled_by_member_id",
                table: "jobs");
        }
    }
}
