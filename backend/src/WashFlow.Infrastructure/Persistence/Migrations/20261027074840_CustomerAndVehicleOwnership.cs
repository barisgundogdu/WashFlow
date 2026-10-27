using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerAndVehicleOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LocalCustomers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    NormalizedPhone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalCustomers", x => x.Id);
                    table.UniqueConstraint("AK_LocalCustomers_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.CheckConstraint("CK_LocalCustomers_FullName", "length(btrim(\"FullName\")) > 0");
                    table.CheckConstraint("CK_LocalCustomers_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_LocalCustomers_NormalizedName", "length(btrim(\"NormalizedName\")) > 0");
                    table.CheckConstraint("CK_LocalCustomers_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_LocalCustomers_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OwnedVehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CustomerProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Plate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NormalizedPlate = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Brand = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModelYear = table.Column<int>(type: "integer", nullable: true),
                    Color = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnedVehicles", x => x.Id);
                    table.CheckConstraint("CK_OwnedVehicles_Archive", "\"ArchivedAt\" IS NULL OR \"ArchivedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_OwnedVehicles_Brand", "length(btrim(\"Brand\")) > 0");
                    table.CheckConstraint("CK_OwnedVehicles_Category", "\"Category\" IN ('Car','Suv','Van','Pickup','Motorcycle','Other')");
                    table.CheckConstraint("CK_OwnedVehicles_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_OwnedVehicles_Model", "length(btrim(\"Model\")) > 0");
                    table.CheckConstraint("CK_OwnedVehicles_NormalizedPlate", "length(btrim(\"NormalizedPlate\")) > 0");
                    table.CheckConstraint("CK_OwnedVehicles_Plate", "length(btrim(\"Plate\")) > 0");
                    table.CheckConstraint("CK_OwnedVehicles_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_OwnedVehicles_Year", "\"ModelYear\" IS NULL OR \"ModelYear\" BETWEEN 1886 AND 2100");
                    table.ForeignKey(
                        name: "FK_OwnedVehicles_CustomerProfiles_CustomerProfileId",
                        column: x => x.CustomerProfileId,
                        principalTable: "CustomerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LocalVehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalCustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Plate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NormalizedPlate = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Brand = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModelYear = table.Column<int>(type: "integer", nullable: true),
                    Color = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalVehicles", x => x.Id);
                    table.CheckConstraint("CK_LocalVehicles_Archive", "\"ArchivedAt\" IS NULL OR \"ArchivedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_LocalVehicles_Brand", "length(btrim(\"Brand\")) > 0");
                    table.CheckConstraint("CK_LocalVehicles_Category", "\"Category\" IN ('Car','Suv','Van','Pickup','Motorcycle','Other')");
                    table.CheckConstraint("CK_LocalVehicles_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_LocalVehicles_Model", "length(btrim(\"Model\")) > 0");
                    table.CheckConstraint("CK_LocalVehicles_NormalizedPlate", "length(btrim(\"NormalizedPlate\")) > 0");
                    table.CheckConstraint("CK_LocalVehicles_Plate", "length(btrim(\"Plate\")) > 0");
                    table.CheckConstraint("CK_LocalVehicles_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_LocalVehicles_Year", "\"ModelYear\" IS NULL OR \"ModelYear\" BETWEEN 1886 AND 2100");
                    table.ForeignKey(
                        name: "FK_LocalVehicles_LocalCustomers_BusinessId_LocalCustomerId",
                        columns: x => new { x.BusinessId, x.LocalCustomerId },
                        principalTable: "LocalCustomers",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LocalCustomers_BusinessId_NormalizedName_Id",
                table: "LocalCustomers",
                columns: new[] { "BusinessId", "NormalizedName", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_LocalCustomers_BusinessId_NormalizedPhone",
                table: "LocalCustomers",
                columns: new[] { "BusinessId", "NormalizedPhone" });

            migrationBuilder.CreateIndex(
                name: "IX_LocalVehicles_BusinessId_LocalCustomerId_Id",
                table: "LocalVehicles",
                columns: new[] { "BusinessId", "LocalCustomerId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_LocalVehicles_BusinessId_NormalizedPlate_Id",
                table: "LocalVehicles",
                columns: new[] { "BusinessId", "NormalizedPlate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OwnedVehicles_CustomerProfileId_NormalizedPlate_Id",
                table: "OwnedVehicles",
                columns: new[] { "CustomerProfileId", "NormalizedPlate", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LocalVehicles");

            migrationBuilder.DropTable(
                name: "OwnedVehicles");

            migrationBuilder.DropTable(
                name: "LocalCustomers");
        }
    }
}
