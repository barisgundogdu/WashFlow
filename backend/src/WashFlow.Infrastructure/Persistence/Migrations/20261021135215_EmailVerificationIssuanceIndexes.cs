using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmailVerificationIssuanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailVerificationTokens_UserId",
                table: "EmailVerificationTokens");

            // Preserve history while retaining only the newest legacy pending credential per user.
            migrationBuilder.Sql("""
                UPDATE "EmailVerificationTokens"
                SET "RevokedAt" = GREATEST(CURRENT_TIMESTAMP, "CreatedAt"),
                    "UpdatedAt" = GREATEST(CURRENT_TIMESTAMP, "CreatedAt")
                WHERE "Id" IN (
                    SELECT "Id" FROM (
                        SELECT "Id", ROW_NUMBER() OVER (
                            PARTITION BY "UserId" ORDER BY "CreatedAt" DESC, "Id" DESC
                        ) AS ordinal
                        FROM "EmailVerificationTokens"
                        WHERE "ConsumedAt" IS NULL AND "RevokedAt" IS NULL
                    ) pending WHERE ordinal > 1
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationTokens_UserId",
                table: "EmailVerificationTokens",
                column: "UserId",
                unique: true,
                filter: "\"ConsumedAt\" IS NULL AND \"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationTokens_UserId_CreatedAt",
                table: "EmailVerificationTokens",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailVerificationTokens_UserId",
                table: "EmailVerificationTokens");

            migrationBuilder.DropIndex(
                name: "IX_EmailVerificationTokens_UserId_CreatedAt",
                table: "EmailVerificationTokens");

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationTokens_UserId",
                table: "EmailVerificationTokens",
                column: "UserId");
        }
    }
}
