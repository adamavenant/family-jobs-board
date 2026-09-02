using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthlyRecurringJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "monthly_day",
                table: "recurring_job_series",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_recurring_job_series_schedule",
                table: "recurring_job_series",
                sql: "(frequency = 'Daily' AND weekday_mask = 0 AND monthly_day IS NULL) OR (frequency = 'Weekly' AND weekday_mask BETWEEN 1 AND 127 AND monthly_day IS NULL) OR (frequency = 'Monthly' AND weekday_mask = 0 AND monthly_day BETWEEN 1 AND 31)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DO $$ BEGIN IF EXISTS " +
                "(SELECT 1 FROM recurring_job_series WHERE frequency = 'Monthly') " +
                "THEN RAISE EXCEPTION 'Cannot downgrade while monthly recurring jobs exist.'; " +
                "END IF; END $$;");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recurring_job_series_schedule",
                table: "recurring_job_series");

            migrationBuilder.DropColumn(
                name: "monthly_day",
                table: "recurring_job_series");
        }
    }
}
