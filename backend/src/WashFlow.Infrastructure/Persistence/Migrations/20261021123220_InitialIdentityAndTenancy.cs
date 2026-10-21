using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentityAndTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Businesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Businesses", x => x.Id);
                    table.CheckConstraint("CK_Businesses_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Businesses_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_Businesses_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SecurityStamp = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_Email", "length(btrim(\"Email\")) > 0");
                    table.CheckConstraint("CK_Users_FullName", "length(btrim(\"FullName\")) > 0");
                    table.CheckConstraint("CK_Users_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Users_NormalizedEmail", "length(btrim(\"NormalizedEmail\")) > 0");
                    table.CheckConstraint("CK_Users_PasswordHash", "length(btrim(\"PasswordHash\")) > 0");
                    table.CheckConstraint("CK_Users_SecurityStamp", "\"SecurityStamp\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Users_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_Users_Verification", "\"EmailVerifiedAt\" IS NULL OR \"EmailVerifiedAt\" >= \"CreatedAt\"");
                });

            migrationBuilder.CreateTable(
                name: "BusinessMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessMemberships", x => x.Id);
                    table.UniqueConstraint("AK_BusinessMemberships_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.CheckConstraint("CK_BusinessMemberships_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_BusinessMemberships_Revocation", "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_BusinessMemberships_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_BusinessMemberships_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessMemberships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerProfiles", x => x.Id);
                    table.CheckConstraint("CK_CustomerProfiles_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_CustomerProfiles_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_CustomerProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeviceSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecurityStamp = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceSessions", x => x.Id);
                    table.CheckConstraint("CK_DeviceSessions_DeviceName", "length(btrim(\"DeviceName\")) > 0");
                    table.CheckConstraint("CK_DeviceSessions_Expiry", "\"ExpiresAt\" > \"CreatedAt\"");
                    table.CheckConstraint("CK_DeviceSessions_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_DeviceSessions_MaxLifetime", "\"ExpiresAt\" <= \"CreatedAt\" + interval '720 hours'");
                    table.CheckConstraint("CK_DeviceSessions_Revocation", "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_DeviceSessions_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_DeviceSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmailVerificationTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    SecurityStamp = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailVerificationTokens", x => x.Id);
                    table.CheckConstraint("CK_EmailVerificationTokens_Consumption", "\"ConsumedAt\" IS NULL OR (\"ConsumedAt\" >= \"CreatedAt\" AND \"ConsumedAt\" < \"ExpiresAt\")");
                    table.CheckConstraint("CK_EmailVerificationTokens_Expiry", "\"ExpiresAt\" > \"CreatedAt\"");
                    table.CheckConstraint("CK_EmailVerificationTokens_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_EmailVerificationTokens_NormalizedEmail", "length(btrim(\"NormalizedEmail\")) > 0");
                    table.CheckConstraint("CK_EmailVerificationTokens_Revocation", "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_EmailVerificationTokens_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_EmailVerificationTokens_TokenHash", "\"TokenHash\" ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_EmailVerificationTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PasswordResetTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecurityStamp = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetTokens", x => x.Id);
                    table.CheckConstraint("CK_PasswordResetTokens_Consumption", "\"ConsumedAt\" IS NULL OR (\"ConsumedAt\" >= \"CreatedAt\" AND \"ConsumedAt\" < \"ExpiresAt\")");
                    table.CheckConstraint("CK_PasswordResetTokens_Expiry", "\"ExpiresAt\" > \"CreatedAt\"");
                    table.CheckConstraint("CK_PasswordResetTokens_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_PasswordResetTokens_Revocation", "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_PasswordResetTokens_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_PasswordResetTokens_TokenHash", "\"TokenHash\" ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_PasswordResetTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessMembershipRoles",
                columns: table => new
                {
                    BusinessMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessMembershipRoles", x => new { x.BusinessMembershipId, x.Role });
                    table.CheckConstraint("CK_BusinessMembershipRoles_Role", "\"Role\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_BusinessMembershipRoles_BusinessMemberships_BusinessMembers~",
                        column: x => x.BusinessMembershipId,
                        principalTable: "BusinessMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitedByMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcceptedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeInvitations", x => x.Id);
                    table.CheckConstraint("CK_EmployeeInvitations_Acceptance", "(\"AcceptedAt\" IS NULL) = (\"AcceptedByUserId\" IS NULL)");
                    table.CheckConstraint("CK_EmployeeInvitations_Consumption", "\"AcceptedAt\" IS NULL OR (\"AcceptedAt\" >= \"CreatedAt\" AND \"AcceptedAt\" < \"ExpiresAt\")");
                    table.CheckConstraint("CK_EmployeeInvitations_Expiry", "\"ExpiresAt\" > \"CreatedAt\"");
                    table.CheckConstraint("CK_EmployeeInvitations_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_EmployeeInvitations_NormalizedEmail", "length(btrim(\"NormalizedEmail\")) > 0");
                    table.CheckConstraint("CK_EmployeeInvitations_Revocation", "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_EmployeeInvitations_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_EmployeeInvitations_TokenHash", "\"TokenHash\" ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_EmployeeInvitations_BusinessMemberships_BusinessId_InvitedB~",
                        columns: x => new { x.BusinessId, x.InvitedByMembershipId },
                        principalTable: "BusinessMemberships",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeInvitations_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeInvitations_Users_AcceptedByUserId",
                        column: x => x.AcceptedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.UniqueConstraint("AK_RefreshTokens_DeviceSessionId_Id", x => new { x.DeviceSessionId, x.Id });
                    table.CheckConstraint("CK_RefreshTokens_Consumption", "\"ConsumedAt\" IS NULL OR \"ConsumedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_RefreshTokens_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_RefreshTokens_Replacement", "\"ReplacedByTokenId\" IS NULL OR (\"ReplacedByTokenId\" <> \"Id\" AND \"ConsumedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_RefreshTokens_Revocation", "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_RefreshTokens_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_RefreshTokens_TokenHash", "\"TokenHash\" ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_RefreshTokens_DeviceSessions_DeviceSessionId",
                        column: x => x.DeviceSessionId,
                        principalTable: "DeviceSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_RefreshTokens_DeviceSessionId_ReplacedByToken~",
                        columns: x => new { x.DeviceSessionId, x.ReplacedByTokenId },
                        principalTable: "RefreshTokens",
                        principalColumns: new[] { "DeviceSessionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessMemberships_BusinessId_UserId",
                table: "BusinessMemberships",
                columns: new[] { "BusinessId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessMemberships_UserId",
                table: "BusinessMemberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerProfiles_UserId",
                table: "CustomerProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSessions_ExpiresAt",
                table: "DeviceSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSessions_UserId",
                table: "DeviceSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationTokens_ExpiresAt",
                table: "EmailVerificationTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationTokens_TokenHash",
                table: "EmailVerificationTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationTokens_UserId",
                table: "EmailVerificationTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeInvitations_AcceptedByUserId",
                table: "EmployeeInvitations",
                column: "AcceptedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeInvitations_BusinessId_InvitedByMembershipId",
                table: "EmployeeInvitations",
                columns: new[] { "BusinessId", "InvitedByMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeInvitations_BusinessId_NormalizedEmail",
                table: "EmployeeInvitations",
                columns: new[] { "BusinessId", "NormalizedEmail" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeInvitations_ExpiresAt",
                table: "EmployeeInvitations",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeInvitations_TokenHash",
                table: "EmployeeInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_ExpiresAt",
                table: "PasswordResetTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_TokenHash",
                table: "PasswordResetTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_UserId",
                table: "PasswordResetTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_DeviceSessionId",
                table: "RefreshTokens",
                column: "DeviceSessionId",
                unique: true,
                filter: "\"ConsumedAt\" IS NULL AND \"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_DeviceSessionId_ReplacedByTokenId",
                table: "RefreshTokens",
                columns: new[] { "DeviceSessionId", "ReplacedByTokenId" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ReplacedByTokenId",
                table: "RefreshTokens",
                column: "ReplacedByTokenId",
                unique: true,
                filter: "\"ReplacedByTokenId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail",
                table: "Users",
                column: "NormalizedEmail",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessMembershipRoles");

            migrationBuilder.DropTable(
                name: "CustomerProfiles");

            migrationBuilder.DropTable(
                name: "EmailVerificationTokens");

            migrationBuilder.DropTable(
                name: "EmployeeInvitations");

            migrationBuilder.DropTable(
                name: "PasswordResetTokens");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "BusinessMemberships");

            migrationBuilder.DropTable(
                name: "DeviceSessions");

            migrationBuilder.DropTable(
                name: "Businesses");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
