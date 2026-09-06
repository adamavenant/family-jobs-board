using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "household_members",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "surname",
                table: "household_members",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE household_members SET role = CASE WHEN is_adult THEN 'Adult' ELSE 'Child' END;");

            migrationBuilder.AlterColumn<string>(
                name: "role",
                table: "household_members",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "is_adult",
                table: "household_members");

            migrationBuilder.AddCheckConstraint(
                name: "ck_household_members_role",
                table: "household_members",
                sql: "role IN ('Adult', 'Child')");

            migrationBuilder.CreateTable(
                name: "auth_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    refresh_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    rotation_version = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_activity_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_auth_sessions_household_members_member_id",
                        column: x => x.member_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "household_bootstrap",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    first_adult_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_household_bootstrap", x => x.id);
                    table.CheckConstraint("ck_household_bootstrap_singleton", "id = 1");
                    table.ForeignKey(
                        name: "FK_household_bootstrap_household_members_first_adult_id",
                        column: x => x.first_adult_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "member_credentials",
                columns: table => new
                {
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    pin_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    pin_set_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_window_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_attempt_count = table.Column<int>(type: "integer", nullable: false),
                    locked_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member_credentials", x => x.member_id);
                    table.CheckConstraint("ck_member_credentials_ready_hash", "(state = 'Ready' AND pin_hash IS NOT NULL) OR (state = 'NotSet' AND pin_hash IS NULL)");
                    table.ForeignKey(
                        name: "FK_member_credentials_household_members_member_id",
                        column: x => x.member_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pin_setup_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    authorizing_adult_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pin_setup_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_pin_setup_tokens_household_members_authorizing_adult_id",
                        column: x => x.authorizing_adult_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pin_setup_tokens_household_members_target_member_id",
                        column: x => x.target_member_id,
                        principalTable: "household_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_sessions_member_id",
                table: "auth_sessions",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "IX_household_bootstrap_first_adult_id",
                table: "household_bootstrap",
                column: "first_adult_id");

            migrationBuilder.CreateIndex(
                name: "IX_pin_setup_tokens_authorizing_adult_id",
                table: "pin_setup_tokens",
                column: "authorizing_adult_id");

            migrationBuilder.CreateIndex(
                name: "IX_pin_setup_tokens_target_member_id",
                table: "pin_setup_tokens",
                column: "target_member_id");

            migrationBuilder.Sql(
                "INSERT INTO household_bootstrap (id, state) VALUES (1, 'Required');");
            migrationBuilder.Sql(
                "INSERT INTO member_credentials (member_id, state, failed_attempt_count) " +
                "SELECT id, 'NotSet', 0 FROM household_members;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM member_credentials WHERE state = 'Ready')
                       OR EXISTS (SELECT 1 FROM auth_sessions)
                       OR EXISTS (SELECT 1 FROM pin_setup_tokens) THEN
                        RAISE EXCEPTION 'Cannot downgrade after authentication data has been created.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "auth_sessions");

            migrationBuilder.DropTable(
                name: "household_bootstrap");

            migrationBuilder.DropTable(
                name: "member_credentials");

            migrationBuilder.DropTable(
                name: "pin_setup_tokens");

            migrationBuilder.DropColumn(
                name: "surname",
                table: "household_members");

            migrationBuilder.AddColumn<bool>(
                name: "is_adult",
                table: "household_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE household_members SET is_adult = (role = 'Adult');");

            migrationBuilder.DropColumn(
                name: "role",
                table: "household_members");
        }
    }
}
