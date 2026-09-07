using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiChildAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assignment_request_id",
                table: "recurring_job_series",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE recurring_job_series SET assignment_request_id = id;");

            migrationBuilder.AlterColumn<Guid>(
                name: "assignment_request_id",
                table: "recurring_job_series",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "household_members",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "ux_recurring_job_series_assignment_request_child",
                table: "recurring_job_series",
                columns: new[] { "assignment_request_id", "child_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_recurring_job_series_assignment_request_child",
                table: "recurring_job_series");

            migrationBuilder.DropColumn(
                name: "assignment_request_id",
                table: "recurring_job_series");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "household_members");
        }
    }
}
