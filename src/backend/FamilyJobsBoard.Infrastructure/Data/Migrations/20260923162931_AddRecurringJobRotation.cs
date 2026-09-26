using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringJobRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "next_turn_index",
                table: "recurring_job_series",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid[]>(
                name: "rotation_child_ids",
                table: "recurring_job_series",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddCheckConstraint(
                name: "ck_recurring_job_series_rotation",
                table: "recurring_job_series",
                sql: "(cardinality(rotation_child_ids) = 0 AND next_turn_index = 0) OR (cardinality(rotation_child_ids) >= 2 AND rotation_child_ids[1] = child_id AND next_turn_index >= 0 AND next_turn_index < cardinality(rotation_child_ids))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_recurring_job_series_rotation",
                table: "recurring_job_series");

            migrationBuilder.DropColumn(
                name: "next_turn_index",
                table: "recurring_job_series");

            migrationBuilder.DropColumn(
                name: "rotation_child_ids",
                table: "recurring_job_series");
        }
    }
}
