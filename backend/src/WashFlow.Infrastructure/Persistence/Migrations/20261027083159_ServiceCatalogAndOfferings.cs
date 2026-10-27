using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceCatalogAndOfferings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessServices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SearchName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Published = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessServices", x => x.Id);
                    table.CheckConstraint("CK_BusinessServices_Archive", "\"ArchivedAt\" IS NULL OR (\"ArchivedAt\" >= \"CreatedAt\" AND NOT \"Enabled\" AND NOT \"Published\")");
                    table.CheckConstraint("CK_BusinessServices_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_BusinessServices_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_BusinessServices_Publication", "NOT \"Published\" OR \"Enabled\"");
                    table.CheckConstraint("CK_BusinessServices_SearchName", "length(btrim(\"SearchName\")) > 0");
                    table.CheckConstraint("CK_BusinessServices_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_BusinessServices_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceOfferings",
                columns: table => new
                {
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ActiveMinutes = table.Column<int>(type: "integer", nullable: false),
                    WaitingMinutes = table.Column<int>(type: "integer", nullable: true),
                    Employees = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceOfferings", x => new { x.ServiceId, x.Category });
                    table.CheckConstraint("CK_ServiceOfferings_Category", "\"Category\" IN ('Car','Suv','Van','Pickup','Motorcycle','Other')");
                    table.CheckConstraint("CK_ServiceOfferings_Currency", "\"Currency\" = 'TRY'");
                    table.CheckConstraint("CK_ServiceOfferings_Estimates", "\"ActiveMinutes\" BETWEEN 1 AND 10080 AND (\"WaitingMinutes\" IS NULL OR \"WaitingMinutes\" BETWEEN 0 AND 43200) AND \"Employees\" BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_ServiceOfferings_Price", "\"Price\" BETWEEN 0 AND 9999999.99 AND scale(\"Price\") <= 2");
                    table.ForeignKey(
                        name: "FK_ServiceOfferings_BusinessServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "BusinessServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessServices_BusinessId_SearchName_Id",
                table: "BusinessServices",
                columns: new[] { "BusinessId", "SearchName", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceOfferings");

            migrationBuilder.DropTable(
                name: "BusinessServices");
        }
    }
}
