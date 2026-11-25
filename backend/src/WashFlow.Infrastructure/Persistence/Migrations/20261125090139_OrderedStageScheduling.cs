using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderedStageScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(name:"PlannedStartUtc",table:"WorkOrders",type:"timestamp with time zone",nullable:true);
            migrationBuilder.DropIndex(
                name: "IX_AppointmentResourceReservations_BusinessId_AppointmentId_Em~",
                table: "AppointmentResourceReservations");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentResourceReservations_BusinessId_AppointmentId_Wo~",
                table: "AppointmentResourceReservations");

            migrationBuilder.AddColumn<Guid>(
                name: "WorkAreaId",
                table: "WorkOrderStages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StagePlanEnabled",
                table: "ServiceSchedulingRequirements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "StageId",
                table: "AppointmentResourceReservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppointmentStages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OffsetMinutes = table.Column<int>(type: "integer", nullable: false),
                    Minutes = table.Column<int>(type: "integer", nullable: false),
                    WorkAreaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Employees = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentStages", x => x.Id);
                    table.UniqueConstraint("AK_AppointmentStages_BusinessId_AppointmentId_Id", x => new { x.BusinessId, x.AppointmentId, x.Id });
                    table.CheckConstraint("CK_AppointmentStages_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_AppointmentStages_Plan", "\"Sequence\" BETWEEN 1 AND 20 AND \"OffsetMinutes\" >= 0 AND length(btrim(\"Name\")) BETWEEN 1 AND 100 AND \"Minutes\" BETWEEN 1 AND 10080 AND \"Employees\" BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_AppointmentStages_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_AppointmentStages_Appointments_BusinessId_AppointmentId",
                        columns: x => new { x.BusinessId, x.AppointmentId },
                        principalTable: "Appointments",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentStages_WorkAreas_BusinessId_WorkAreaId",
                        columns: x => new { x.BusinessId, x.WorkAreaId },
                        principalTable: "WorkAreas",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceStageTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Minutes = table.Column<int>(type: "integer", nullable: false),
                    WorkAreaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Employees = table.Column<int>(type: "integer", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceStageTemplates", x => x.Id);
                    table.UniqueConstraint("AK_ServiceStageTemplates_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.CheckConstraint("CK_ServiceStageTemplates_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_ServiceStageTemplates_Plan", "\"Sequence\" BETWEEN 1 AND 20 AND length(btrim(\"Name\")) BETWEEN 1 AND 100 AND \"Minutes\" BETWEEN 1 AND 10080 AND \"Employees\" BETWEEN 1 AND 100 AND (\"ArchivedAt\" IS NULL OR \"ArchivedAt\" >= \"CreatedAt\") AND \"Category\" IN ('Car','Suv','Van','Pickup','Motorcycle','Other')");
                    table.CheckConstraint("CK_ServiceStageTemplates_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_ServiceStageTemplates_BusinessServices_BusinessId_ServiceId",
                        columns: x => new { x.BusinessId, x.ServiceId },
                        principalTable: "BusinessServices",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceStageTemplates_WorkAreas_BusinessId_WorkAreaId",
                        columns: x => new { x.BusinessId, x.WorkAreaId },
                        principalTable: "WorkAreas",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderStages_BusinessId_WorkAreaId",
                table: "WorkOrderStages",
                columns: new[] { "BusinessId", "WorkAreaId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentResourceReservations_BusinessId_AppointmentId_Em~",
                table: "AppointmentResourceReservations",
                columns: new[] { "BusinessId", "AppointmentId", "EmployeeMembershipId" },
                unique: true,
                filter: "\"ReleasedAt\" IS NULL AND \"EmployeeMembershipId\" IS NOT NULL AND \"StageId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentResourceReservations_BusinessId_AppointmentId_S~1",
                table: "AppointmentResourceReservations",
                columns: new[] { "BusinessId", "AppointmentId", "StageId", "WorkAreaId" },
                unique: true,
                filter: "\"ReleasedAt\" IS NULL AND \"WorkAreaId\" IS NOT NULL AND \"StageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentResourceReservations_BusinessId_AppointmentId_St~",
                table: "AppointmentResourceReservations",
                columns: new[] { "BusinessId", "AppointmentId", "StageId", "EmployeeMembershipId" },
                unique: true,
                filter: "\"ReleasedAt\" IS NULL AND \"EmployeeMembershipId\" IS NOT NULL AND \"StageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentResourceReservations_BusinessId_AppointmentId_Wo~",
                table: "AppointmentResourceReservations",
                columns: new[] { "BusinessId", "AppointmentId", "WorkAreaId" },
                unique: true,
                filter: "\"ReleasedAt\" IS NULL AND \"WorkAreaId\" IS NOT NULL AND \"StageId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentStages_BusinessId_AppointmentId_Sequence",
                table: "AppointmentStages",
                columns: new[] { "BusinessId", "AppointmentId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentStages_BusinessId_WorkAreaId",
                table: "AppointmentStages",
                columns: new[] { "BusinessId", "WorkAreaId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceStageTemplates_BusinessId_ServiceId_Category_Sequence",
                table: "ServiceStageTemplates",
                columns: new[] { "BusinessId", "ServiceId", "Category", "Sequence" },
                unique: true,
                filter: "\"ArchivedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceStageTemplates_BusinessId_WorkAreaId",
                table: "ServiceStageTemplates",
                columns: new[] { "BusinessId", "WorkAreaId" });

            migrationBuilder.AddForeignKey(
                name: "FK_AppointmentResourceReservations_AppointmentStages_BusinessI~",
                table: "AppointmentResourceReservations",
                columns: new[] { "BusinessId", "AppointmentId", "StageId" },
                principalTable: "AppointmentStages",
                principalColumns: new[] { "BusinessId", "AppointmentId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrderStages_WorkAreas_BusinessId_WorkAreaId",
                table: "WorkOrderStages",
                columns: new[] { "BusinessId", "WorkAreaId" },
                principalTable: "WorkAreas",
                principalColumns: new[] { "BusinessId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.CreateIndex(name:"IX_WorkOrderStages_BusinessId_WorkOrderId",table:"WorkOrderStages",columns:new[]{"BusinessId","WorkOrderId"},unique:true,filter:"\"Status\" IN (3,4)");
            migrationBuilder.Sql("""
                CREATE FUNCTION washflow_work_order_identity() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF (to_jsonb(NEW) - ARRAY['UpdatedAt','CompletedAtUtc','CancelledAtUtc']) IS DISTINCT FROM (to_jsonb(OLD) - ARRAY['UpdatedAt','CompletedAtUtc','CancelledAtUtc'])
                        OR (OLD."CompletedAtUtc" IS NOT NULL AND NEW."CompletedAtUtc" IS DISTINCT FROM OLD."CompletedAtUtc")
                        OR (OLD."CancelledAtUtc" IS NOT NULL AND NEW."CancelledAtUtc" IS DISTINCT FROM OLD."CancelledAtUtc") THEN
                        RAISE EXCEPTION 'İş emri kimliği ve tarihçesi değiştirilemez.' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $body$;
                CREATE TRIGGER work_order_identity_immutable BEFORE UPDATE ON "WorkOrders" FOR EACH ROW EXECUTE FUNCTION washflow_work_order_identity();
                """);
            migrationBuilder.Sql("CREATE TRIGGER appointment_stage_immutable BEFORE UPDATE OR DELETE ON \"AppointmentStages\" FOR EACH ROW EXECUTE FUNCTION washflow_scheduling_immutable()");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"AppointmentStages\") OR EXISTS (SELECT 1 FROM \"ServiceStageTemplates\") OR EXISTS (SELECT 1 FROM \"WorkOrders\" WHERE \"PlannedStartUtc\" IS NOT NULL) THEN RAISE EXCEPTION 'Aşama verileri varken otomatik geri dönüş desteklenmiyor; incelenmiş veri koruma planı gerekli.'; END IF; END $$");
            migrationBuilder.Sql("DROP TRIGGER work_order_identity_immutable ON \"WorkOrders\"; DROP FUNCTION washflow_work_order_identity()");
            migrationBuilder.DropColumn(name:"PlannedStartUtc",table:"WorkOrders");
            migrationBuilder.DropIndex(name:"IX_WorkOrderStages_BusinessId_WorkOrderId",table:"WorkOrderStages");
            migrationBuilder.DropForeignKey(
                name: "FK_AppointmentResourceReservations_AppointmentStages_BusinessI~",
                table: "AppointmentResourceReservations");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrderStages_WorkAreas_BusinessId_WorkAreaId",
                table: "WorkOrderStages");

            migrationBuilder.DropTable(
                name: "AppointmentStages");

            migrationBuilder.DropTable(
                name: "ServiceStageTemplates");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrderStages_BusinessId_WorkAreaId",
                table: "WorkOrderStages");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentResourceReservations_BusinessId_AppointmentId_Em~",
                table: "AppointmentResourceReservations");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentResourceReservations_BusinessId_AppointmentId_S~1",
                table: "AppointmentResourceReservations");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentResourceReservations_BusinessId_AppointmentId_St~",
                table: "AppointmentResourceReservations");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentResourceReservations_BusinessId_AppointmentId_Wo~",
                table: "AppointmentResourceReservations");

            migrationBuilder.DropColumn(
                name: "WorkAreaId",
                table: "WorkOrderStages");

            migrationBuilder.DropColumn(
                name: "StagePlanEnabled",
                table: "ServiceSchedulingRequirements");

            migrationBuilder.DropColumn(
                name: "StageId",
                table: "AppointmentResourceReservations");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentResourceReservations_BusinessId_AppointmentId_Em~",
                table: "AppointmentResourceReservations",
                columns: new[] { "BusinessId", "AppointmentId", "EmployeeMembershipId" },
                unique: true,
                filter: "\"ReleasedAt\" IS NULL AND \"EmployeeMembershipId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentResourceReservations_BusinessId_AppointmentId_Wo~",
                table: "AppointmentResourceReservations",
                columns: new[] { "BusinessId", "AppointmentId", "WorkAreaId" },
                unique: true,
                filter: "\"ReleasedAt\" IS NULL AND \"WorkAreaId\" IS NOT NULL");
        }
    }
}
