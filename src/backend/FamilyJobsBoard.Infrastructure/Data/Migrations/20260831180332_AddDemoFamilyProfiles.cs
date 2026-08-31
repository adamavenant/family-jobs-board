using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyJobsBoard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDemoFamilyProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO household_members (id, first_name, nickname, is_adult)
                VALUES
                    ('754de05d-b6f6-4626-bbad-79e2079cc5c3', 'Fredster', NULL, FALSE),
                    ('e22facf5-69ce-45ce-9dad-306eef1852c9', 'Harrie', NULL, FALSE)
                ON CONFLICT (id) DO NOTHING;

                UPDATE household_members
                SET first_name = 'Addie', nickname = NULL, is_adult = TRUE
                WHERE id = '22eb0cc1-058e-4b2e-bb18-d7aaad564a6c';

                UPDATE household_members
                SET first_name = 'Hellie', nickname = NULL, is_adult = TRUE
                WHERE id = '9db319c1-28d1-4ce6-93d7-f04a45f8257d';

                UPDATE jobs
                SET child_id = '754de05d-b6f6-4626-bbad-79e2079cc5c3'
                WHERE child_id = '22eb0cc1-058e-4b2e-bb18-d7aaad564a6c';

                UPDATE points_ledger_entries
                SET child_id = '754de05d-b6f6-4626-bbad-79e2079cc5c3'
                WHERE child_id = '22eb0cc1-058e-4b2e-bb18-d7aaad564a6c';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE jobs
                SET child_id = '22eb0cc1-058e-4b2e-bb18-d7aaad564a6c'
                WHERE child_id IN (
                    '754de05d-b6f6-4626-bbad-79e2079cc5c3',
                    'e22facf5-69ce-45ce-9dad-306eef1852c9');

                UPDATE points_ledger_entries
                SET child_id = '22eb0cc1-058e-4b2e-bb18-d7aaad564a6c'
                WHERE child_id IN (
                    '754de05d-b6f6-4626-bbad-79e2079cc5c3',
                    'e22facf5-69ce-45ce-9dad-306eef1852c9');

                DELETE FROM household_members
                WHERE id IN (
                    '754de05d-b6f6-4626-bbad-79e2079cc5c3',
                    'e22facf5-69ce-45ce-9dad-306eef1852c9');

                UPDATE household_members
                SET first_name = 'Addie', nickname = NULL, is_adult = FALSE
                WHERE id = '22eb0cc1-058e-4b2e-bb18-d7aaad564a6c';

                UPDATE household_members
                SET first_name = 'Adam', nickname = NULL, is_adult = TRUE
                WHERE id = '9db319c1-28d1-4ce6-93d7-f04a45f8257d';
                """);
        }
    }
}
