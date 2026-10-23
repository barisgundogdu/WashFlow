using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeInvitationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmployeeInvitations_BusinessId_NormalizedEmail",
                table: "EmployeeInvitations");

            // Revoke older legacy duplicates before enforcing uniqueness.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id", row_number() OVER (PARTITION BY "BusinessId", "NormalizedEmail"
                        ORDER BY "CreatedAt" DESC, "Id" DESC) AS rank
                    FROM "EmployeeInvitations" WHERE "AcceptedAt" IS NULL AND "RevokedAt" IS NULL
                )
                UPDATE "EmployeeInvitations" i
                SET "RevokedAt" = GREATEST(CURRENT_TIMESTAMP, i."CreatedAt"),
                    "UpdatedAt" = GREATEST(CURRENT_TIMESTAMP, i."UpdatedAt", i."CreatedAt")
                FROM ranked r WHERE i."Id" = r."Id" AND r.rank > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeInvitations_BusinessId_NormalizedEmail",
                table: "EmployeeInvitations",
                columns: new[] { "BusinessId", "NormalizedEmail" },
                unique: true,
                filter: "\"AcceptedAt\" IS NULL AND \"RevokedAt\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmployeeInvitations_FinalState",
                table: "EmployeeInvitations",
                sql: "\"AcceptedAt\" IS NULL OR \"RevokedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmployeeInvitations_BusinessId_NormalizedEmail",
                table: "EmployeeInvitations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EmployeeInvitations_FinalState",
                table: "EmployeeInvitations");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeInvitations_BusinessId_NormalizedEmail",
                table: "EmployeeInvitations",
                columns: new[] { "BusinessId", "NormalizedEmail" });
        }
    }
}
