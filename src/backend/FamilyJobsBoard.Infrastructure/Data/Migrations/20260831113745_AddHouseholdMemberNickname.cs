using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseholdMemberNickname : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "nickname",
                table: "household_members",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE household_members SET first_name = 'Addie' " +
                "WHERE id = '22eb0cc1-058e-4b2e-bb18-d7aaad564a6c' AND first_name = 'Alex';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE household_members SET first_name = 'Alex' " +
                "WHERE id = '22eb0cc1-058e-4b2e-bb18-d7aaad564a6c' AND first_name = 'Addie';");

            migrationBuilder.DropColumn(
                name: "nickname",
                table: "household_members");
        }
    }
}
