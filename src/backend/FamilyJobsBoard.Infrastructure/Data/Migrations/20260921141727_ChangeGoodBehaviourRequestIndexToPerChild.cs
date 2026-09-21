using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChangeGoodBehaviourRequestIndexToPerChild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_good_behaviours_request_id",
                table: "good_behaviours");

            migrationBuilder.CreateIndex(
                name: "ux_good_behaviours_request_id_child_id",
                table: "good_behaviours",
                columns: new[] { "request_id", "child_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_good_behaviours_request_id_child_id",
                table: "good_behaviours");

            migrationBuilder.CreateIndex(
                name: "ux_good_behaviours_request_id",
                table: "good_behaviours",
                column: "request_id",
                unique: true);
        }
    }
}
