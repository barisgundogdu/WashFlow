using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OwnerFinancialLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CorrectsId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversesId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EmployeeMembershipId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.UniqueConstraint("AK_Expenses_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.CheckConstraint("CK_Expenses_Category", "length(btrim(\"Category\")) > 0");
                    table.CheckConstraint("CK_Expenses_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Expenses_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_Expenses_Values", "\"Amount\" BETWEEN 0.01 AND 9999999.99 AND \"Currency\" = 'TRY' AND \"RequestHash\" ~ '^[0-9a-f]{64}$' AND \"RequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND ((\"ReversesId\" IS NULL AND \"CorrectionReason\" IS NULL) OR (\"ReversesId\" IS NOT NULL AND \"ReversesId\" <> \"Id\" AND length(btrim(\"CorrectionReason\")) > 0))");
                    table.ForeignKey(
                        name: "FK_Expenses_BusinessMemberships_BusinessId_EmployeeMembershipId",
                        columns: x => new { x.BusinessId, x.EmployeeMembershipId },
                        principalTable: "BusinessMemberships",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Expenses_BusinessId_CorrectsId",
                        columns: x => new { x.BusinessId, x.CorrectsId },
                        principalTable: "Expenses",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Expenses_BusinessId_ReversesId",
                        columns: x => new { x.BusinessId, x.ReversesId },
                        principalTable: "Expenses",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethods", x => x.Id);
                    table.UniqueConstraint("AK_PaymentMethods_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.CheckConstraint("CK_PaymentMethods_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_PaymentMethods_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_PaymentMethods_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_PaymentMethods_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentMethods_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManualIncomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CorrectsId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversesId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PaymentMethodId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentMethodName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualIncomes", x => x.Id);
                    table.UniqueConstraint("AK_ManualIncomes_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.CheckConstraint("CK_ManualIncomes_Category", "length(btrim(\"Category\")) > 0");
                    table.CheckConstraint("CK_ManualIncomes_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_ManualIncomes_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_ManualIncomes_Values", "\"Amount\" BETWEEN 0.01 AND 9999999.99 AND \"Currency\" = 'TRY' AND \"RequestHash\" ~ '^[0-9a-f]{64}$' AND \"RequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND ((\"ReversesId\" IS NULL AND \"CorrectionReason\" IS NULL) OR (\"ReversesId\" IS NOT NULL AND \"ReversesId\" <> \"Id\" AND length(btrim(\"CorrectionReason\")) > 0))");
                    table.ForeignKey(
                        name: "FK_ManualIncomes_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManualIncomes_ManualIncomes_BusinessId_CorrectsId",
                        columns: x => new { x.BusinessId, x.CorrectsId },
                        principalTable: "ManualIncomes",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManualIncomes_ManualIncomes_BusinessId_ReversesId",
                        columns: x => new { x.BusinessId, x.ReversesId },
                        principalTable: "ManualIncomes",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManualIncomes_PaymentMethods_BusinessId_PaymentMethodId",
                        columns: x => new { x.BusinessId, x.PaymentMethodId },
                        principalTable: "PaymentMethods",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManualIncomes_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CorrectsId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversesId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentMethodId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentMethodName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.UniqueConstraint("AK_Payments_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.CheckConstraint("CK_Payments_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Payments_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_Payments_Values", "\"Amount\" BETWEEN 0.01 AND 9999999.99 AND \"Currency\" = 'TRY' AND \"RequestHash\" ~ '^[0-9a-f]{64}$' AND \"RequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND ((\"ReversesId\" IS NULL AND \"CorrectionReason\" IS NULL) OR (\"ReversesId\" IS NOT NULL AND \"ReversesId\" <> \"Id\" AND length(btrim(\"CorrectionReason\")) > 0))");
                    table.ForeignKey(
                        name: "FK_Payments_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_PaymentMethods_BusinessId_PaymentMethodId",
                        columns: x => new { x.BusinessId, x.PaymentMethodId },
                        principalTable: "PaymentMethods",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Payments_BusinessId_CorrectsId",
                        columns: x => new { x.BusinessId, x.CorrectsId },
                        principalTable: "Payments",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Payments_BusinessId_ReversesId",
                        columns: x => new { x.BusinessId, x.ReversesId },
                        principalTable: "Payments",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_WorkOrders_BusinessId_WorkOrderId",
                        columns: x => new { x.BusinessId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ActorUserId",
                table: "Expenses",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BusinessId_CorrectsId",
                table: "Expenses",
                columns: new[] { "BusinessId", "CorrectsId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BusinessId_EmployeeMembershipId",
                table: "Expenses",
                columns: new[] { "BusinessId", "EmployeeMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BusinessId_OccurredAtUtc_Id",
                table: "Expenses",
                columns: new[] { "BusinessId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BusinessId_RequestId",
                table: "Expenses",
                columns: new[] { "BusinessId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BusinessId_ReversesId",
                table: "Expenses",
                columns: new[] { "BusinessId", "ReversesId" },
                unique: true,
                filter: "\"ReversesId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ManualIncomes_ActorUserId",
                table: "ManualIncomes",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ManualIncomes_BusinessId_CorrectsId",
                table: "ManualIncomes",
                columns: new[] { "BusinessId", "CorrectsId" });

            migrationBuilder.CreateIndex(
                name: "IX_ManualIncomes_BusinessId_OccurredAtUtc_Id",
                table: "ManualIncomes",
                columns: new[] { "BusinessId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ManualIncomes_BusinessId_PaymentMethodId",
                table: "ManualIncomes",
                columns: new[] { "BusinessId", "PaymentMethodId" });

            migrationBuilder.CreateIndex(
                name: "IX_ManualIncomes_BusinessId_RequestId",
                table: "ManualIncomes",
                columns: new[] { "BusinessId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManualIncomes_BusinessId_ReversesId",
                table: "ManualIncomes",
                columns: new[] { "BusinessId", "ReversesId" },
                unique: true,
                filter: "\"ReversesId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_ActorUserId",
                table: "PaymentMethods",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_BusinessId_Name",
                table: "PaymentMethods",
                columns: new[] { "BusinessId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ActorUserId",
                table: "Payments",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_BusinessId_CorrectsId",
                table: "Payments",
                columns: new[] { "BusinessId", "CorrectsId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_BusinessId_OccurredAtUtc_Id",
                table: "Payments",
                columns: new[] { "BusinessId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_BusinessId_PaymentMethodId",
                table: "Payments",
                columns: new[] { "BusinessId", "PaymentMethodId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_BusinessId_RequestId",
                table: "Payments",
                columns: new[] { "BusinessId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_BusinessId_ReversesId",
                table: "Payments",
                columns: new[] { "BusinessId", "ReversesId" },
                unique: true,
                filter: "\"ReversesId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_BusinessId_WorkOrderId",
                table: "Payments",
                columns: new[] { "BusinessId", "WorkOrderId" });

            migrationBuilder.Sql("""
                CREATE FUNCTION washflow_finance_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE original jsonb; omitted text[] := ARRAY['Id','RequestId','RequestHash','CreatedAt','UpdatedAt','ActorUserId','ReversesId','CorrectionReason'];
                BEGIN
                    IF TG_OP <> 'INSERT' THEN RAISE EXCEPTION 'Financial history is append-only' USING ERRCODE='23514'; END IF;
                    IF NEW."ReversesId" IS NOT NULL THEN
                        EXECUTE format('SELECT to_jsonb(r) FROM %I r WHERE "BusinessId"=$1 AND "Id"=$2', TG_TABLE_NAME) INTO original USING NEW."BusinessId", NEW."ReversesId";
                        IF original IS NULL OR original->>'ReversesId' IS NOT NULL OR (original - omitted) IS DISTINCT FROM (to_jsonb(NEW) - omitted) THEN
                            RAISE EXCEPTION 'Reversal must exactly negate an original financial entry' USING ERRCODE='23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER finance_payments_immutable BEFORE INSERT OR UPDATE OR DELETE ON "Payments" FOR EACH ROW EXECUTE FUNCTION washflow_finance_append_only();
                CREATE TRIGGER finance_manual_income_immutable BEFORE INSERT OR UPDATE OR DELETE ON "ManualIncomes" FOR EACH ROW EXECUTE FUNCTION washflow_finance_append_only();
                CREATE TRIGGER finance_expenses_immutable BEFORE INSERT OR UPDATE OR DELETE ON "Expenses" FOR EACH ROW EXECUTE FUNCTION washflow_finance_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS(SELECT 1 FROM "Payments") OR EXISTS(SELECT 1 FROM "ManualIncomes") OR EXISTS(SELECT 1 FROM "Expenses") OR EXISTS(SELECT 1 FROM "PaymentMethods") THEN
                    RAISE EXCEPTION 'Financial records exist; data-preserving resolution is required before downgrade';
                  END IF;
                END $$;
                DROP TRIGGER finance_payments_immutable ON "Payments";
                DROP TRIGGER finance_manual_income_immutable ON "ManualIncomes";
                DROP TRIGGER finance_expenses_immutable ON "Expenses";
                DROP FUNCTION washflow_finance_append_only();
                """);
            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "ManualIncomes");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "PaymentMethods");
        }
    }
}
