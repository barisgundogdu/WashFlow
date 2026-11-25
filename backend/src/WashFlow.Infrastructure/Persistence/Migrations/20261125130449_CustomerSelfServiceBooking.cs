using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerSelfServiceBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_State",
                table: "Appointments");

            migrationBuilder.AddColumn<int>(
                name: "CancellationRescheduleCutoffMinutes",
                table: "Businesses",
                type: "integer",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.AlterColumn<Guid>(
                name: "LocalVehicleId",
                table: "Appointments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "LocalCustomerId",
                table: "Appointments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerProfileId",
                table: "Appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnedVehicleId",
                table: "Appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_OwnedVehicles_CustomerProfileId_Id",
                table: "OwnedVehicles",
                columns: new[] { "CustomerProfileId", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Businesses_BookingCutoff",
                table: "Businesses",
                sql: "\"CancellationRescheduleCutoffMinutes\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_CustomerProfileId_OwnedVehicleId",
                table: "Appointments",
                columns: new[] { "CustomerProfileId", "OwnedVehicleId" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_CustomerProfileId_StartsAtUtc_Id",
                table: "Appointments",
                columns: new[] { "CustomerProfileId", "StartsAtUtc", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_State",
                table: "Appointments",
                sql: "\"Status\" IN (1,2) AND ((\"Source\" = 1 AND \"LocalCustomerId\" IS NOT NULL AND \"LocalVehicleId\" IS NOT NULL AND \"CustomerProfileId\" IS NULL AND \"OwnedVehicleId\" IS NULL) OR (\"Source\" = 2 AND \"LocalCustomerId\" IS NULL AND \"LocalVehicleId\" IS NULL AND \"CustomerProfileId\" IS NOT NULL AND \"OwnedVehicleId\" IS NOT NULL))");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_CustomerProfiles_CustomerProfileId",
                table: "Appointments",
                column: "CustomerProfileId",
                principalTable: "CustomerProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_OwnedVehicles_CustomerProfileId_OwnedVehicleId",
                table: "Appointments",
                columns: new[] { "CustomerProfileId", "OwnedVehicleId" },
                principalTable: "OwnedVehicles",
                principalColumns: new[] { "CustomerProfileId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "Appointments" WHERE "Source" = 2)
                     OR EXISTS (SELECT 1 FROM "Businesses" WHERE "CancellationRescheduleCutoffMinutes" <> 120) THEN
                    RAISE EXCEPTION 'Customer booking history or customized policy exists; explicit data-preserving resolution is required before downgrade';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_CustomerProfiles_CustomerProfileId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_OwnedVehicles_CustomerProfileId_OwnedVehicleId",
                table: "Appointments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OwnedVehicles_CustomerProfileId_Id",
                table: "OwnedVehicles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Businesses_BookingCutoff",
                table: "Businesses");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_CustomerProfileId_OwnedVehicleId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_CustomerProfileId_StartsAtUtc_Id",
                table: "Appointments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_State",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "CancellationRescheduleCutoffMinutes",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "CustomerProfileId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "OwnedVehicleId",
                table: "Appointments");

            migrationBuilder.AlterColumn<Guid>(
                name: "LocalVehicleId",
                table: "Appointments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "LocalCustomerId",
                table: "Appointments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_State",
                table: "Appointments",
                sql: "\"Status\" IN (1,2) AND \"Source\" = 1");
        }
    }
}
