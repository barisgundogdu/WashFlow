using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SchedulingPersistencePhase9B : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_LocalVehicles_BusinessId_LocalCustomerId_Id",
                table: "LocalVehicles",
                columns: new[] { "BusinessId", "LocalCustomerId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_BusinessServices_BusinessId_Id",
                table: "BusinessServices",
                columns: new[] { "BusinessId", "Id" });

            migrationBuilder.CreateTable(
                name: "EmployeeAvailabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeAvailabilities", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeAvailabilities_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.CheckConstraint("CK_EmployeeAvailabilities_Archive", "\"ArchivedAt\" IS NULL OR \"ArchivedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_EmployeeAvailabilities_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_EmployeeAvailabilities_Interval", "\"StartsAtUtc\" < \"EndsAtUtc\" AND date_trunc('minute', \"StartsAtUtc\") = \"StartsAtUtc\" AND date_trunc('minute', \"EndsAtUtc\") = \"EndsAtUtc\"");
                    table.CheckConstraint("CK_EmployeeAvailabilities_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_EmployeeAvailabilities_BusinessMemberships_BusinessId_Membe~",
                        columns: x => new { x.BusinessId, x.MembershipId },
                        principalTable: "BusinessMemberships",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeAvailabilities_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkAreas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkAreas", x => x.Id);
                    table.UniqueConstraint("AK_WorkAreas_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.CheckConstraint("CK_WorkAreas_Archive", "\"ArchivedAt\" IS NULL OR \"ArchivedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_WorkAreas_Capacity", "\"Capacity\" > 0");
                    table.CheckConstraint("CK_WorkAreas_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_WorkAreas_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_WorkAreas_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_WorkAreas_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LocalCustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalVehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkAreaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActiveEndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimeZoneIdSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ServiceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VehiclePlate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ActiveMinutes = table.Column<int>(type: "integer", nullable: false),
                    WaitingMinutes = table.Column<int>(type: "integer", nullable: true),
                    Employees = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.UniqueConstraint("AK_Appointments_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.CheckConstraint("CK_Appointments_Category", "length(btrim(\"Category\")) > 0");
                    table.CheckConstraint("CK_Appointments_Currency", "length(btrim(\"Currency\")) > 0");
                    table.CheckConstraint("CK_Appointments_CustomerName", "length(btrim(\"CustomerName\")) > 0");
                    table.CheckConstraint("CK_Appointments_Hash", "\"RequestHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_Appointments_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Appointments_Interval", "\"StartsAtUtc\" < \"ActiveEndsAtUtc\" AND date_trunc('minute', \"StartsAtUtc\") = \"StartsAtUtc\" AND date_trunc('minute', \"ActiveEndsAtUtc\") = \"ActiveEndsAtUtc\"");
                    table.CheckConstraint("CK_Appointments_RequestHash", "length(btrim(\"RequestHash\")) > 0");
                    table.CheckConstraint("CK_Appointments_ServiceName", "length(btrim(\"ServiceName\")) > 0");
                    table.CheckConstraint("CK_Appointments_Snapshot", "\"Price\" BETWEEN 0 AND 9999999.99 AND \"Currency\" = 'TRY' AND \"ActiveMinutes\" BETWEEN 1 AND 10080 AND (\"WaitingMinutes\" IS NULL OR \"WaitingMinutes\" BETWEEN 0 AND 43200) AND \"Employees\" BETWEEN 1 AND 100 AND \"ActiveEndsAtUtc\" = \"StartsAtUtc\" + \"ActiveMinutes\" * interval '1 minute'");
                    table.CheckConstraint("CK_Appointments_State", "\"Status\" IN (1,2) AND \"Source\" = 1");
                    table.CheckConstraint("CK_Appointments_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_Appointments_TimeZoneIdSnapshot", "length(btrim(\"TimeZoneIdSnapshot\")) > 0");
                    table.CheckConstraint("CK_Appointments_VehiclePlate", "length(btrim(\"VehiclePlate\")) > 0");
                    table.ForeignKey(
                        name: "FK_Appointments_BusinessServices_BusinessId_ServiceId",
                        columns: x => new { x.BusinessId, x.ServiceId },
                        principalTable: "BusinessServices",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_LocalCustomers_BusinessId_LocalCustomerId",
                        columns: x => new { x.BusinessId, x.LocalCustomerId },
                        principalTable: "LocalCustomers",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_LocalVehicles_BusinessId_LocalCustomerId_Local~",
                        columns: x => new { x.BusinessId, x.LocalCustomerId, x.LocalVehicleId },
                        principalTable: "LocalVehicles",
                        principalColumns: new[] { "BusinessId", "LocalCustomerId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_WorkAreas_BusinessId_WorkAreaId",
                        columns: x => new { x.BusinessId, x.WorkAreaId },
                        principalTable: "WorkAreas",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceSchedulingRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WorkAreaId = table.Column<Guid>(type: "uuid", nullable: false),
                    SingleStageConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceSchedulingRequirements", x => x.Id);
                    table.UniqueConstraint("AK_ServiceSchedulingRequirements_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.CheckConstraint("CK_ServiceSchedulingRequirements_Category", "\"Category\" IN ('Car','Suv','Van','Pickup','Motorcycle','Other')");
                    table.CheckConstraint("CK_ServiceSchedulingRequirements_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_ServiceSchedulingRequirements_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_ServiceSchedulingRequirements_BusinessServices_BusinessId_S~",
                        columns: x => new { x.BusinessId, x.ServiceId },
                        principalTable: "BusinessServices",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceSchedulingRequirements_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceSchedulingRequirements_WorkAreas_BusinessId_WorkArea~",
                        columns: x => new { x.BusinessId, x.WorkAreaId },
                        principalTable: "WorkAreas",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppointmentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    PreviousStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PreviousEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NewStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NewEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentEvents", x => x.Id);
                    table.UniqueConstraint("AK_AppointmentEvents_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.CheckConstraint("CK_AppointmentEvents_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_AppointmentEvents_State", "\"Sequence\" > 0 AND \"Kind\" IN (1,2,3) AND \"Status\" IN (1,2) AND \"NewStart\" < \"NewEnd\" AND ((\"PreviousStart\" IS NULL AND \"PreviousEnd\" IS NULL) OR (\"PreviousStart\" IS NOT NULL AND \"PreviousEnd\" IS NOT NULL AND \"PreviousStart\" < \"PreviousEnd\"))");
                    table.CheckConstraint("CK_AppointmentEvents_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_AppointmentEvents_Appointments_BusinessId_AppointmentId",
                        columns: x => new { x.BusinessId, x.AppointmentId },
                        principalTable: "Appointments",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentEvents_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentEvents_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppointmentResourceReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkAreaId = table.Column<Guid>(type: "uuid", nullable: true),
                    EmployeeMembershipId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentResourceReservations", x => x.Id);
                    table.UniqueConstraint("AK_AppointmentResourceReservations_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.CheckConstraint("CK_AppointmentResourceReservations_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_AppointmentResourceReservations_Interval", "\"StartsAtUtc\" < \"EndsAtUtc\" AND date_trunc('minute', \"StartsAtUtc\") = \"StartsAtUtc\" AND date_trunc('minute', \"EndsAtUtc\") = \"EndsAtUtc\"");
                    table.CheckConstraint("CK_AppointmentResourceReservations_Release", "\"ReleasedAt\" IS NULL OR \"ReleasedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_AppointmentResourceReservations_Resource", "(\"WorkAreaId\" IS NULL) <> (\"EmployeeMembershipId\" IS NULL)");
                    table.CheckConstraint("CK_AppointmentResourceReservations_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_AppointmentResourceReservations_Appointments_BusinessId_App~",
                        columns: x => new { x.BusinessId, x.AppointmentId },
                        principalTable: "Appointments",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentResourceReservations_BusinessMemberships_Busines~",
                        columns: x => new { x.BusinessId, x.EmployeeMembershipId },
                        principalTable: "BusinessMemberships",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentResourceReservations_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentResourceReservations_WorkAreas_BusinessId_WorkAr~",
                        columns: x => new { x.BusinessId, x.WorkAreaId },
                        principalTable: "WorkAreas",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SchedulingCreateReceipts",
                columns: table => new
                {
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulingCreateReceipts", x => new { x.BusinessId, x.ClientRequestId });
                    table.ForeignKey(
                        name: "FK_SchedulingCreateReceipts_Appointments_BusinessId_Appointmen~",
                        columns: x => new { x.BusinessId, x.AppointmentId },
                        principalTable: "Appointments",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentEvents_ActorUserId",
                table: "AppointmentEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentEvents_BusinessId_AppointmentId_Sequence",
                table: "AppointmentEvents",
                columns: new[] { "BusinessId", "AppointmentId", "Sequence" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentResourceReservations_BusinessId_EmployeeMembersh~",
                table: "AppointmentResourceReservations",
                columns: new[] { "BusinessId", "EmployeeMembershipId", "StartsAtUtc", "EndsAtUtc" },
                filter: "\"ReleasedAt\" IS NULL AND \"EmployeeMembershipId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentResourceReservations_BusinessId_WorkAreaId_Start~",
                table: "AppointmentResourceReservations",
                columns: new[] { "BusinessId", "WorkAreaId", "StartsAtUtc", "EndsAtUtc" },
                filter: "\"ReleasedAt\" IS NULL AND \"WorkAreaId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BusinessId_ClientRequestId",
                table: "Appointments",
                columns: new[] { "BusinessId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BusinessId_LocalCustomerId_LocalVehicleId",
                table: "Appointments",
                columns: new[] { "BusinessId", "LocalCustomerId", "LocalVehicleId" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BusinessId_ServiceId",
                table: "Appointments",
                columns: new[] { "BusinessId", "ServiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BusinessId_StartsAtUtc_Id",
                table: "Appointments",
                columns: new[] { "BusinessId", "StartsAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BusinessId_Status_StartsAtUtc",
                table: "Appointments",
                columns: new[] { "BusinessId", "Status", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BusinessId_WorkAreaId",
                table: "Appointments",
                columns: new[] { "BusinessId", "WorkAreaId" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_CreatedByUserId",
                table: "Appointments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAvailabilities_BusinessId_MembershipId_StartsAtUtc_~",
                table: "EmployeeAvailabilities",
                columns: new[] { "BusinessId", "MembershipId", "StartsAtUtc", "EndsAtUtc" },
                filter: "\"ArchivedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingCreateReceipts_BusinessId_AppointmentId",
                table: "SchedulingCreateReceipts",
                columns: new[] { "BusinessId", "AppointmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceSchedulingRequirements_BusinessId_ServiceId_Category",
                table: "ServiceSchedulingRequirements",
                columns: new[] { "BusinessId", "ServiceId", "Category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceSchedulingRequirements_BusinessId_WorkAreaId",
                table: "ServiceSchedulingRequirements",
                columns: new[] { "BusinessId", "WorkAreaId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkAreas_BusinessId_Name_Id",
                table: "WorkAreas",
                columns: new[] { "BusinessId", "Name", "Id" });
            // PostgreSQL guards also preserve snapshots/history against accidental direct writes.
            migrationBuilder.Sql("""
                CREATE FUNCTION washflow_scheduling_immutable() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    RAISE EXCEPTION 'Planlama geçmişi değiştirilemez.' USING ERRCODE = '23514';
                END;
                $body$;
                CREATE TRIGGER scheduling_event_immutable BEFORE UPDATE OR DELETE ON "AppointmentEvents"
                    FOR EACH ROW EXECUTE FUNCTION washflow_scheduling_immutable();
                CREATE TRIGGER scheduling_receipt_immutable BEFORE UPDATE OR DELETE ON "SchedulingCreateReceipts"
                    FOR EACH ROW EXECUTE FUNCTION washflow_scheduling_immutable();
                CREATE FUNCTION washflow_appointment_snapshot() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF (to_jsonb(NEW) - ARRAY['UpdatedAt','Status','StartsAtUtc','ActiveEndsAtUtc'])
                        IS DISTINCT FROM (to_jsonb(OLD) - ARRAY['UpdatedAt','Status','StartsAtUtc','ActiveEndsAtUtc'])
                        OR (OLD."Status" = 2 AND NEW."Status" <> 2) THEN
                        RAISE EXCEPTION 'Randevu anlık değerleri değiştirilemez.' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $body$;
                CREATE TRIGGER scheduling_snapshot_immutable BEFORE UPDATE ON "Appointments"
                    FOR EACH ROW EXECUTE FUNCTION washflow_appointment_snapshot();
                CREATE FUNCTION washflow_reservation_history() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Kaynak geçmişi silinemez.' USING ERRCODE = '23514';
                    END IF;
                    IF (to_jsonb(NEW) - ARRAY['UpdatedAt','ReleasedAt']) IS DISTINCT FROM (to_jsonb(OLD) - ARRAY['UpdatedAt','ReleasedAt'])
                        OR (OLD."ReleasedAt" IS NOT NULL AND NEW."ReleasedAt" IS DISTINCT FROM OLD."ReleasedAt") THEN
                        RAISE EXCEPTION 'Kaynak geçmişi değiştirilemez.' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $body$;
                CREATE TRIGGER scheduling_reservation_history BEFORE UPDATE OR DELETE ON "AppointmentResourceReservations"
                    FOR EACH ROW EXECUTE FUNCTION washflow_reservation_history();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER scheduling_event_immutable ON "AppointmentEvents";
                DROP TRIGGER scheduling_receipt_immutable ON "SchedulingCreateReceipts";
                DROP TRIGGER scheduling_snapshot_immutable ON "Appointments";
                DROP TRIGGER scheduling_reservation_history ON "AppointmentResourceReservations";
                DROP FUNCTION washflow_scheduling_immutable();
                DROP FUNCTION washflow_appointment_snapshot();
                DROP FUNCTION washflow_reservation_history();
                """);
            migrationBuilder.DropTable(
                name: "AppointmentEvents");

            migrationBuilder.DropTable(
                name: "AppointmentResourceReservations");

            migrationBuilder.DropTable(
                name: "EmployeeAvailabilities");

            migrationBuilder.DropTable(
                name: "SchedulingCreateReceipts");

            migrationBuilder.DropTable(
                name: "ServiceSchedulingRequirements");

            migrationBuilder.DropTable(
                name: "Appointments");

            migrationBuilder.DropTable(
                name: "WorkAreas");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_LocalVehicles_BusinessId_LocalCustomerId_Id",
                table: "LocalVehicles");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_BusinessServices_BusinessId_Id",
                table: "BusinessServices");
        }
    }
}
