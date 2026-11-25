using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkOrderExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.Id);
                    table.UniqueConstraint("AK_WorkOrders_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.CheckConstraint("CK_WorkOrders_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_WorkOrders_Lifecycle", "NOT (\"CompletedAtUtc\" IS NOT NULL AND \"CancelledAtUtc\" IS NOT NULL) AND (\"CompletedAtUtc\" IS NULL OR \"CompletedAtUtc\" >= \"CreatedAt\") AND (\"CancelledAtUtc\" IS NULL OR \"CancelledAtUtc\" >= \"CreatedAt\")");
                    table.CheckConstraint("CK_WorkOrders_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_WorkOrders_Appointments_BusinessId_AppointmentId",
                        columns: x => new { x.BusinessId, x.AppointmentId },
                        principalTable: "Appointments",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderStages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PausedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PausedMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderStages", x => x.Id);
                    table.UniqueConstraint("AK_WorkOrderStages_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.UniqueConstraint("AK_WorkOrderStages_BusinessId_WorkOrderId_Id", x => new { x.BusinessId, x.WorkOrderId, x.Id });
                    table.CheckConstraint("CK_WorkOrderStages_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_WorkOrderStages_State", "\"Sequence\" > 0 AND length(btrim(\"Name\")) > 0 AND \"EstimatedMinutes\" BETWEEN 1 AND 10080 AND \"Status\" BETWEEN 1 AND 6 AND \"PausedMilliseconds\" >= 0 AND ((\"Status\" IN (1,2) AND \"StartedAtUtc\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"PausedAtUtc\" IS NULL) OR (\"Status\" = 6 AND \"CompletedAtUtc\" IS NULL AND \"PausedAtUtc\" IS NULL) OR (\"Status\" = 3 AND \"StartedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" IS NULL AND \"PausedAtUtc\" IS NULL) OR (\"Status\" = 4 AND \"StartedAtUtc\" IS NOT NULL AND \"PausedAtUtc\" IS NOT NULL AND \"PausedAtUtc\" >= \"StartedAtUtc\" AND \"CompletedAtUtc\" IS NULL) OR (\"Status\" = 5 AND \"StartedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" >= \"StartedAtUtc\" AND \"PausedAtUtc\" IS NULL))");
                    table.CheckConstraint("CK_WorkOrderStages_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_WorkOrderStages_WorkOrders_BusinessId_WorkOrderId",
                        columns: x => new { x.BusinessId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderEvents", x => x.Id);
                    table.CheckConstraint("CK_WorkOrderEvents_Action", "\"RequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"Action\" IN ('create','start','pause','resume','complete','cancel')");
                    table.ForeignKey(
                        name: "FK_WorkOrderEvents_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderEvents_WorkOrderStages_BusinessId_WorkOrderId_Stag~",
                        columns: x => new { x.BusinessId, x.WorkOrderId, x.StageId },
                        principalTable: "WorkOrderStages",
                        principalColumns: new[] { "BusinessId", "WorkOrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderEvents_WorkOrders_BusinessId_WorkOrderId",
                        columns: x => new { x.BusinessId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkStageAssignments",
                columns: table => new
                {
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkStageAssignments", x => new { x.BusinessId, x.StageId, x.EmployeeMembershipId });
                    table.ForeignKey(
                        name: "FK_WorkStageAssignments_BusinessMemberships_BusinessId_Employe~",
                        columns: x => new { x.BusinessId, x.EmployeeMembershipId },
                        principalTable: "BusinessMemberships",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkStageAssignments_WorkOrderStages_BusinessId_StageId",
                        columns: x => new { x.BusinessId, x.StageId },
                        principalTable: "WorkOrderStages",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderEvents_ActorUserId",
                table: "WorkOrderEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderEvents_BusinessId_RequestId",
                table: "WorkOrderEvents",
                columns: new[] { "BusinessId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderEvents_BusinessId_WorkOrderId_StageId",
                table: "WorkOrderEvents",
                columns: new[] { "BusinessId", "WorkOrderId", "StageId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderEvents_BusinessId_WorkOrderId",
                table: "WorkOrderEvents",
                columns: new[] { "BusinessId", "WorkOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_BusinessId_AppointmentId",
                table: "WorkOrders",
                columns: new[] { "BusinessId", "AppointmentId" },
                unique: true,
                filter: "\"CancelledAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderStages_BusinessId_WorkOrderId_Sequence",
                table: "WorkOrderStages",
                columns: new[] { "BusinessId", "WorkOrderId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkStageAssignments_BusinessId_EmployeeMembershipId_StageId",
                table: "WorkStageAssignments",
                columns: new[] { "BusinessId", "EmployeeMembershipId", "StageId" });
            migrationBuilder.Sql("CREATE TRIGGER work_order_event_immutable BEFORE UPDATE OR DELETE ON \"WorkOrderEvents\" FOR EACH ROW EXECUTE FUNCTION washflow_scheduling_immutable()");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkOrderEvents");

            migrationBuilder.DropTable(
                name: "WorkStageAssignments");

            migrationBuilder.DropTable(
                name: "WorkOrderStages");

            migrationBuilder.DropTable(
                name: "WorkOrders");
        }
    }
}
