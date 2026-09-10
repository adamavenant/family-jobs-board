using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deactivated_at_utc",
                table: "household_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deactivated_by_member_id",
                table: "household_members",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "profile_updated_at_utc",
                table: "household_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "profile_updated_by_member_id",
                table: "household_members",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "restored_at_utc",
                table: "household_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "restored_by_member_id",
                table: "household_members",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_household_members_deactivated_by_member_id",
                table: "household_members",
                column: "deactivated_by_member_id");

            migrationBuilder.CreateIndex(
                name: "IX_household_members_profile_updated_by_member_id",
                table: "household_members",
                column: "profile_updated_by_member_id");

            migrationBuilder.CreateIndex(
                name: "IX_household_members_restored_by_member_id",
                table: "household_members",
                column: "restored_by_member_id");

            migrationBuilder.AddForeignKey(
                name: "FK_household_members_household_members_deactivated_by_member_id",
                table: "household_members",
                column: "deactivated_by_member_id",
                principalTable: "household_members",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_household_members_household_members_profile_updated_by_memb~",
                table: "household_members",
                column: "profile_updated_by_member_id",
                principalTable: "household_members",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_household_members_household_members_restored_by_member_id",
                table: "household_members",
                column: "restored_by_member_id",
                principalTable: "household_members",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_household_members_household_members_deactivated_by_member_id",
                table: "household_members");

            migrationBuilder.DropForeignKey(
                name: "FK_household_members_household_members_profile_updated_by_memb~",
                table: "household_members");

            migrationBuilder.DropForeignKey(
                name: "FK_household_members_household_members_restored_by_member_id",
                table: "household_members");

            migrationBuilder.DropIndex(
                name: "IX_household_members_deactivated_by_member_id",
                table: "household_members");

            migrationBuilder.DropIndex(
                name: "IX_household_members_profile_updated_by_member_id",
                table: "household_members");

            migrationBuilder.DropIndex(
                name: "IX_household_members_restored_by_member_id",
                table: "household_members");

            migrationBuilder.DropColumn(
                name: "deactivated_at_utc",
                table: "household_members");

            migrationBuilder.DropColumn(
                name: "deactivated_by_member_id",
                table: "household_members");

            migrationBuilder.DropColumn(
                name: "profile_updated_at_utc",
                table: "household_members");

            migrationBuilder.DropColumn(
                name: "profile_updated_by_member_id",
                table: "household_members");

            migrationBuilder.DropColumn(
                name: "restored_at_utc",
                table: "household_members");

            migrationBuilder.DropColumn(
                name: "restored_by_member_id",
                table: "household_members");
        }
    }
}
