START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125082321_WorkOrderExecution') THEN
    CREATE TABLE "WorkOrders" (
        "Id" uuid NOT NULL,
        "BusinessId" uuid NOT NULL,
        "AppointmentId" uuid NOT NULL,
        "CreationHash" character varying(64) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "CompletedAtUtc" timestamp with time zone,
        "CancelledAtUtc" timestamp with time zone,
        CONSTRAINT "PK_WorkOrders" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_WorkOrders_BusinessId_Id" UNIQUE ("BusinessId", "Id"),
        CONSTRAINT "CK_WorkOrders_Id" CHECK ("Id" <> '00000000-0000-0000-0000-000000000000'::uuid),
        CONSTRAINT "CK_WorkOrders_Lifecycle" CHECK (NOT ("CompletedAtUtc" IS NOT NULL AND "CancelledAtUtc" IS NOT NULL) AND ("CompletedAtUtc" IS NULL OR "CompletedAtUtc" >= "CreatedAt") AND ("CancelledAtUtc" IS NULL OR "CancelledAtUtc" >= "CreatedAt")),
        CONSTRAINT "CK_WorkOrders_Timestamps" CHECK ("UpdatedAt" >= "CreatedAt"),
        CONSTRAINT "FK_WorkOrders_Appointments_BusinessId_AppointmentId" FOREIGN KEY ("BusinessId", "AppointmentId") REFERENCES "Appointments" ("BusinessId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125082321_WorkOrderExecution') THEN
    CREATE TABLE "WorkOrderStages" (
        "Id" uuid NOT NULL,
        "BusinessId" uuid NOT NULL,
        "WorkOrderId" uuid NOT NULL,
        "Sequence" integer NOT NULL,
        "Name" character varying(100) NOT NULL,
        "EstimatedMinutes" integer NOT NULL,
        "Status" integer NOT NULL,
        "StartedAtUtc" timestamp with time zone,
        "CompletedAtUtc" timestamp with time zone,
        "PausedAtUtc" timestamp with time zone,
        "PausedMilliseconds" bigint NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_WorkOrderStages" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_WorkOrderStages_BusinessId_Id" UNIQUE ("BusinessId", "Id"),
        CONSTRAINT "AK_WorkOrderStages_BusinessId_WorkOrderId_Id" UNIQUE ("BusinessId", "WorkOrderId", "Id"),
        CONSTRAINT "CK_WorkOrderStages_Id" CHECK ("Id" <> '00000000-0000-0000-0000-000000000000'::uuid),
        CONSTRAINT "CK_WorkOrderStages_State" CHECK ("Sequence" > 0 AND length(btrim("Name")) > 0 AND "EstimatedMinutes" BETWEEN 1 AND 10080 AND "Status" BETWEEN 1 AND 6 AND "PausedMilliseconds" >= 0 AND (("Status" IN (1,2) AND "StartedAtUtc" IS NULL AND "CompletedAtUtc" IS NULL AND "PausedAtUtc" IS NULL) OR ("Status" = 6 AND "CompletedAtUtc" IS NULL AND "PausedAtUtc" IS NULL) OR ("Status" = 3 AND "StartedAtUtc" IS NOT NULL AND "CompletedAtUtc" IS NULL AND "PausedAtUtc" IS NULL) OR ("Status" = 4 AND "StartedAtUtc" IS NOT NULL AND "PausedAtUtc" IS NOT NULL AND "PausedAtUtc" >= "StartedAtUtc" AND "CompletedAtUtc" IS NULL) OR ("Status" = 5 AND "StartedAtUtc" IS NOT NULL AND "CompletedAtUtc" IS NOT NULL AND "CompletedAtUtc" >= "StartedAtUtc" AND "PausedAtUtc" IS NULL))),
        CONSTRAINT "CK_WorkOrderStages_Timestamps" CHECK ("UpdatedAt" >= "CreatedAt"),
        CONSTRAINT "FK_WorkOrderStages_WorkOrders_BusinessId_WorkOrderId" FOREIGN KEY ("BusinessId", "WorkOrderId") REFERENCES "WorkOrders" ("BusinessId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125082321_WorkOrderExecution') THEN
    CREATE TABLE "WorkOrderEvents" (
        "Id" uuid NOT NULL,
        "BusinessId" uuid NOT NULL,
        "WorkOrderId" uuid NOT NULL,
        "StageId" uuid,
        "ActorUserId" uuid NOT NULL,
        "RequestId" uuid NOT NULL,
        "Action" character varying(20) NOT NULL,
        "AtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_WorkOrderEvents" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_WorkOrderEvents_Action" CHECK ("RequestId" <> '00000000-0000-0000-0000-000000000000'::uuid AND "Action" IN ('create','start','pause','resume','complete','cancel')),
        CONSTRAINT "FK_WorkOrderEvents_Users_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_WorkOrderEvents_WorkOrderStages_BusinessId_WorkOrderId_Stag~" FOREIGN KEY ("BusinessId", "WorkOrderId", "StageId") REFERENCES "WorkOrderStages" ("BusinessId", "WorkOrderId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_WorkOrderEvents_WorkOrders_BusinessId_WorkOrderId" FOREIGN KEY ("BusinessId", "WorkOrderId") REFERENCES "WorkOrders" ("BusinessId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125082321_WorkOrderExecution') THEN
    CREATE TABLE "WorkStageAssignments" (
        "BusinessId" uuid NOT NULL,
        "StageId" uuid NOT NULL,
        "EmployeeMembershipId" uuid NOT NULL,
        "AssignedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_WorkStageAssignments" PRIMARY KEY ("BusinessId", "StageId", "EmployeeMembershipId"),
        CONSTRAINT "FK_WorkStageAssignments_BusinessMemberships_BusinessId_Employe~" FOREIGN KEY ("BusinessId", "EmployeeMembershipId") REFERENCES "BusinessMemberships" ("BusinessId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_WorkStageAssignments_WorkOrderStages_BusinessId_StageId" FOREIGN KEY ("BusinessId", "StageId") REFERENCES "WorkOrderStages" ("BusinessId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125082321_WorkOrderExecution') THEN
    CREATE INDEX "IX_WorkOrderEvents_ActorUserId" ON "WorkOrderEvents" ("ActorUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125082321_WorkOrderExecution') THEN
    CREATE UNIQUE INDEX "IX_WorkOrderEvents_BusinessId_RequestId" ON "WorkOrderEvents" ("BusinessId", "RequestId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125082321_WorkOrderExecution') THEN
    CREATE INDEX "IX_WorkOrderEvents_BusinessId_WorkOrderId_StageId" ON "WorkOrderEvents" ("BusinessId", "WorkOrderId", "StageId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125082321_WorkOrderExecution') THEN
    CREATE INDEX "IX_WorkOrderEvents_BusinessId_WorkOrderId" ON "WorkOrderEvents" ("BusinessId", "WorkOrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125082321_WorkOrderExecution') THEN
    CREATE UNIQUE INDEX "IX_WorkOrders_BusinessId_AppointmentId" ON "WorkOrders" ("BusinessId", "AppointmentId") WHERE "CancelledAtUtc" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125082321_WorkOrderExecution') THEN
    CREATE UNIQUE INDEX "IX_WorkOrderStages_BusinessId_WorkOrderId_Sequence" ON "WorkOrderStages" ("BusinessId", "WorkOrderId", "Sequence");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125082321_WorkOrderExecution') THEN
    CREATE INDEX "IX_WorkStageAssignments_BusinessId_EmployeeMembershipId_StageId" ON "WorkStageAssignments" ("BusinessId", "EmployeeMembershipId", "StageId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125082321_WorkOrderExecution') THEN
    CREATE TRIGGER work_order_event_immutable BEFORE UPDATE OR DELETE ON "WorkOrderEvents" FOR EACH ROW EXECUTE FUNCTION washflow_scheduling_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125082321_WorkOrderExecution') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261125082321_WorkOrderExecution', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    ALTER TABLE "WorkOrders" ADD "PlannedStartUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    DROP INDEX "IX_AppointmentResourceReservations_BusinessId_AppointmentId_Em~";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    DROP INDEX "IX_AppointmentResourceReservations_BusinessId_AppointmentId_Wo~";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    ALTER TABLE "WorkOrderStages" ADD "WorkAreaId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    ALTER TABLE "ServiceSchedulingRequirements" ADD "StagePlanEnabled" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    ALTER TABLE "AppointmentResourceReservations" ADD "StageId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    CREATE TABLE "AppointmentStages" (
        "Id" uuid NOT NULL,
        "BusinessId" uuid NOT NULL,
        "AppointmentId" uuid NOT NULL,
        "Sequence" integer NOT NULL,
        "Name" character varying(100) NOT NULL,
        "OffsetMinutes" integer NOT NULL,
        "Minutes" integer NOT NULL,
        "WorkAreaId" uuid NOT NULL,
        "Employees" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_AppointmentStages" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_AppointmentStages_BusinessId_AppointmentId_Id" UNIQUE ("BusinessId", "AppointmentId", "Id"),
        CONSTRAINT "CK_AppointmentStages_Id" CHECK ("Id" <> '00000000-0000-0000-0000-000000000000'::uuid),
        CONSTRAINT "CK_AppointmentStages_Plan" CHECK ("Sequence" BETWEEN 1 AND 20 AND "OffsetMinutes" >= 0 AND length(btrim("Name")) BETWEEN 1 AND 100 AND "Minutes" BETWEEN 1 AND 10080 AND "Employees" BETWEEN 1 AND 100),
        CONSTRAINT "CK_AppointmentStages_Timestamps" CHECK ("UpdatedAt" >= "CreatedAt"),
        CONSTRAINT "FK_AppointmentStages_Appointments_BusinessId_AppointmentId" FOREIGN KEY ("BusinessId", "AppointmentId") REFERENCES "Appointments" ("BusinessId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AppointmentStages_WorkAreas_BusinessId_WorkAreaId" FOREIGN KEY ("BusinessId", "WorkAreaId") REFERENCES "WorkAreas" ("BusinessId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    CREATE TABLE "ServiceStageTemplates" (
        "Id" uuid NOT NULL,
        "BusinessId" uuid NOT NULL,
        "ServiceId" uuid NOT NULL,
        "Category" character varying(32) NOT NULL,
        "Sequence" integer NOT NULL,
        "Name" character varying(100) NOT NULL,
        "Minutes" integer NOT NULL,
        "WorkAreaId" uuid NOT NULL,
        "Employees" integer NOT NULL,
        "ArchivedAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_ServiceStageTemplates" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_ServiceStageTemplates_BusinessId_Id" UNIQUE ("BusinessId", "Id"),
        CONSTRAINT "CK_ServiceStageTemplates_Id" CHECK ("Id" <> '00000000-0000-0000-0000-000000000000'::uuid),
        CONSTRAINT "CK_ServiceStageTemplates_Plan" CHECK ("Sequence" BETWEEN 1 AND 20 AND length(btrim("Name")) BETWEEN 1 AND 100 AND "Minutes" BETWEEN 1 AND 10080 AND "Employees" BETWEEN 1 AND 100 AND ("ArchivedAt" IS NULL OR "ArchivedAt" >= "CreatedAt") AND "Category" IN ('Car','Suv','Van','Pickup','Motorcycle','Other')),
        CONSTRAINT "CK_ServiceStageTemplates_Timestamps" CHECK ("UpdatedAt" >= "CreatedAt"),
        CONSTRAINT "FK_ServiceStageTemplates_BusinessServices_BusinessId_ServiceId" FOREIGN KEY ("BusinessId", "ServiceId") REFERENCES "BusinessServices" ("BusinessId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_ServiceStageTemplates_WorkAreas_BusinessId_WorkAreaId" FOREIGN KEY ("BusinessId", "WorkAreaId") REFERENCES "WorkAreas" ("BusinessId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    CREATE INDEX "IX_WorkOrderStages_BusinessId_WorkAreaId" ON "WorkOrderStages" ("BusinessId", "WorkAreaId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    CREATE UNIQUE INDEX "IX_AppointmentResourceReservations_BusinessId_AppointmentId_Em~" ON "AppointmentResourceReservations" ("BusinessId", "AppointmentId", "EmployeeMembershipId") WHERE "ReleasedAt" IS NULL AND "EmployeeMembershipId" IS NOT NULL AND "StageId" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    CREATE UNIQUE INDEX "IX_AppointmentResourceReservations_BusinessId_AppointmentId_S~1" ON "AppointmentResourceReservations" ("BusinessId", "AppointmentId", "StageId", "WorkAreaId") WHERE "ReleasedAt" IS NULL AND "WorkAreaId" IS NOT NULL AND "StageId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    CREATE UNIQUE INDEX "IX_AppointmentResourceReservations_BusinessId_AppointmentId_St~" ON "AppointmentResourceReservations" ("BusinessId", "AppointmentId", "StageId", "EmployeeMembershipId") WHERE "ReleasedAt" IS NULL AND "EmployeeMembershipId" IS NOT NULL AND "StageId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    CREATE UNIQUE INDEX "IX_AppointmentResourceReservations_BusinessId_AppointmentId_Wo~" ON "AppointmentResourceReservations" ("BusinessId", "AppointmentId", "WorkAreaId") WHERE "ReleasedAt" IS NULL AND "WorkAreaId" IS NOT NULL AND "StageId" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    CREATE UNIQUE INDEX "IX_AppointmentStages_BusinessId_AppointmentId_Sequence" ON "AppointmentStages" ("BusinessId", "AppointmentId", "Sequence");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    CREATE INDEX "IX_AppointmentStages_BusinessId_WorkAreaId" ON "AppointmentStages" ("BusinessId", "WorkAreaId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    CREATE UNIQUE INDEX "IX_ServiceStageTemplates_BusinessId_ServiceId_Category_Sequence" ON "ServiceStageTemplates" ("BusinessId", "ServiceId", "Category", "Sequence") WHERE "ArchivedAt" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    CREATE INDEX "IX_ServiceStageTemplates_BusinessId_WorkAreaId" ON "ServiceStageTemplates" ("BusinessId", "WorkAreaId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    ALTER TABLE "AppointmentResourceReservations" ADD CONSTRAINT "FK_AppointmentResourceReservations_AppointmentStages_BusinessI~" FOREIGN KEY ("BusinessId", "AppointmentId", "StageId") REFERENCES "AppointmentStages" ("BusinessId", "AppointmentId", "Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    ALTER TABLE "WorkOrderStages" ADD CONSTRAINT "FK_WorkOrderStages_WorkAreas_BusinessId_WorkAreaId" FOREIGN KEY ("BusinessId", "WorkAreaId") REFERENCES "WorkAreas" ("BusinessId", "Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    CREATE UNIQUE INDEX "IX_WorkOrderStages_BusinessId_WorkOrderId" ON "WorkOrderStages" ("BusinessId", "WorkOrderId") WHERE "Status" IN (3,4);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    CREATE TRIGGER appointment_stage_immutable BEFORE UPDATE OR DELETE ON "AppointmentStages" FOR EACH ROW EXECUTE FUNCTION washflow_scheduling_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090139_OrderedStageScheduling') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261125090139_OrderedStageScheduling', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125130449_CustomerSelfServiceBooking') THEN
    ALTER TABLE "Appointments" DROP CONSTRAINT "CK_Appointments_State";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125130449_CustomerSelfServiceBooking') THEN
    ALTER TABLE "Businesses" ADD "CancellationRescheduleCutoffMinutes" integer NOT NULL DEFAULT 120;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125130449_CustomerSelfServiceBooking') THEN
    ALTER TABLE "Appointments" ALTER COLUMN "LocalVehicleId" DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125130449_CustomerSelfServiceBooking') THEN
    ALTER TABLE "Appointments" ALTER COLUMN "LocalCustomerId" DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125130449_CustomerSelfServiceBooking') THEN
    ALTER TABLE "Appointments" ADD "CustomerProfileId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125130449_CustomerSelfServiceBooking') THEN
    ALTER TABLE "Appointments" ADD "OwnedVehicleId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125130449_CustomerSelfServiceBooking') THEN
    ALTER TABLE "OwnedVehicles" ADD CONSTRAINT "AK_OwnedVehicles_CustomerProfileId_Id" UNIQUE ("CustomerProfileId", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125130449_CustomerSelfServiceBooking') THEN
    ALTER TABLE "Businesses" ADD CONSTRAINT "CK_Businesses_BookingCutoff" CHECK ("CancellationRescheduleCutoffMinutes" >= 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125130449_CustomerSelfServiceBooking') THEN
    CREATE INDEX "IX_Appointments_CustomerProfileId_OwnedVehicleId" ON "Appointments" ("CustomerProfileId", "OwnedVehicleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125130449_CustomerSelfServiceBooking') THEN
    CREATE INDEX "IX_Appointments_CustomerProfileId_StartsAtUtc_Id" ON "Appointments" ("CustomerProfileId", "StartsAtUtc", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125130449_CustomerSelfServiceBooking') THEN
    ALTER TABLE "Appointments" ADD CONSTRAINT "CK_Appointments_State" CHECK ("Status" IN (1,2) AND (("Source" = 1 AND "LocalCustomerId" IS NOT NULL AND "LocalVehicleId" IS NOT NULL AND "CustomerProfileId" IS NULL AND "OwnedVehicleId" IS NULL) OR ("Source" = 2 AND "LocalCustomerId" IS NULL AND "LocalVehicleId" IS NULL AND "CustomerProfileId" IS NOT NULL AND "OwnedVehicleId" IS NOT NULL)));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125130449_CustomerSelfServiceBooking') THEN
    ALTER TABLE "Appointments" ADD CONSTRAINT "FK_Appointments_CustomerProfiles_CustomerProfileId" FOREIGN KEY ("CustomerProfileId") REFERENCES "CustomerProfiles" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125130449_CustomerSelfServiceBooking') THEN
    ALTER TABLE "Appointments" ADD CONSTRAINT "FK_Appointments_OwnedVehicles_CustomerProfileId_OwnedVehicleId" FOREIGN KEY ("CustomerProfileId", "OwnedVehicleId") REFERENCES "OwnedVehicles" ("CustomerProfileId", "Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125130449_CustomerSelfServiceBooking') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261125130449_CustomerSelfServiceBooking', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE TABLE "Expenses" (
        "Id" uuid NOT NULL,
        "BusinessId" uuid NOT NULL,
        "RequestId" uuid NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        "Amount" numeric(9,2) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "ActorUserId" uuid NOT NULL,
        "Description" character varying(1000),
        "Reference" character varying(200),
        "CorrectsId" uuid,
        "ReversesId" uuid,
        "CorrectionReason" character varying(500),
        "Category" character varying(100) NOT NULL,
        "EmployeeMembershipId" uuid,
        CONSTRAINT "PK_Expenses" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_Expenses_BusinessId_Id" UNIQUE ("BusinessId", "Id"),
        CONSTRAINT "CK_Expenses_Category" CHECK (length(btrim("Category")) > 0),
        CONSTRAINT "CK_Expenses_Id" CHECK ("Id" <> '00000000-0000-0000-0000-000000000000'::uuid),
        CONSTRAINT "CK_Expenses_Timestamps" CHECK ("UpdatedAt" >= "CreatedAt"),
        CONSTRAINT "CK_Expenses_Values" CHECK ("Amount" BETWEEN 0.01 AND 9999999.99 AND "Currency" = 'TRY' AND "RequestHash" ~ '^[0-9a-f]{64}$' AND "RequestId" <> '00000000-0000-0000-0000-000000000000'::uuid AND (("ReversesId" IS NULL AND "CorrectionReason" IS NULL) OR ("ReversesId" IS NOT NULL AND "ReversesId" <> "Id" AND length(btrim("CorrectionReason")) > 0))),
        CONSTRAINT "FK_Expenses_BusinessMemberships_BusinessId_EmployeeMembershipId" FOREIGN KEY ("BusinessId", "EmployeeMembershipId") REFERENCES "BusinessMemberships" ("BusinessId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Expenses_Businesses_BusinessId" FOREIGN KEY ("BusinessId") REFERENCES "Businesses" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Expenses_Expenses_BusinessId_CorrectsId" FOREIGN KEY ("BusinessId", "CorrectsId") REFERENCES "Expenses" ("BusinessId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Expenses_Expenses_BusinessId_ReversesId" FOREIGN KEY ("BusinessId", "ReversesId") REFERENCES "Expenses" ("BusinessId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Expenses_Users_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE TABLE "PaymentMethods" (
        "Id" uuid NOT NULL,
        "BusinessId" uuid NOT NULL,
        "Name" character varying(100) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "ArchivedAtUtc" timestamp with time zone,
        "ActorUserId" uuid NOT NULL,
        CONSTRAINT "PK_PaymentMethods" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_PaymentMethods_BusinessId_Id" UNIQUE ("BusinessId", "Id"),
        CONSTRAINT "CK_PaymentMethods_Id" CHECK ("Id" <> '00000000-0000-0000-0000-000000000000'::uuid),
        CONSTRAINT "CK_PaymentMethods_Name" CHECK (length(btrim("Name")) > 0),
        CONSTRAINT "CK_PaymentMethods_Timestamps" CHECK ("UpdatedAt" >= "CreatedAt"),
        CONSTRAINT "FK_PaymentMethods_Businesses_BusinessId" FOREIGN KEY ("BusinessId") REFERENCES "Businesses" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_PaymentMethods_Users_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE TABLE "ManualIncomes" (
        "Id" uuid NOT NULL,
        "BusinessId" uuid NOT NULL,
        "RequestId" uuid NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        "Amount" numeric(9,2) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "ActorUserId" uuid NOT NULL,
        "Description" character varying(1000),
        "Reference" character varying(200),
        "CorrectsId" uuid,
        "ReversesId" uuid,
        "CorrectionReason" character varying(500),
        "Category" character varying(100) NOT NULL,
        "PaymentMethodId" uuid NOT NULL,
        "PaymentMethodName" character varying(100) NOT NULL,
        CONSTRAINT "PK_ManualIncomes" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_ManualIncomes_BusinessId_Id" UNIQUE ("BusinessId", "Id"),
        CONSTRAINT "CK_ManualIncomes_Category" CHECK (length(btrim("Category")) > 0),
        CONSTRAINT "CK_ManualIncomes_Id" CHECK ("Id" <> '00000000-0000-0000-0000-000000000000'::uuid),
        CONSTRAINT "CK_ManualIncomes_Timestamps" CHECK ("UpdatedAt" >= "CreatedAt"),
        CONSTRAINT "CK_ManualIncomes_Values" CHECK ("Amount" BETWEEN 0.01 AND 9999999.99 AND "Currency" = 'TRY' AND "RequestHash" ~ '^[0-9a-f]{64}$' AND "RequestId" <> '00000000-0000-0000-0000-000000000000'::uuid AND (("ReversesId" IS NULL AND "CorrectionReason" IS NULL) OR ("ReversesId" IS NOT NULL AND "ReversesId" <> "Id" AND length(btrim("CorrectionReason")) > 0))),
        CONSTRAINT "FK_ManualIncomes_Businesses_BusinessId" FOREIGN KEY ("BusinessId") REFERENCES "Businesses" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_ManualIncomes_ManualIncomes_BusinessId_CorrectsId" FOREIGN KEY ("BusinessId", "CorrectsId") REFERENCES "ManualIncomes" ("BusinessId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_ManualIncomes_ManualIncomes_BusinessId_ReversesId" FOREIGN KEY ("BusinessId", "ReversesId") REFERENCES "ManualIncomes" ("BusinessId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_ManualIncomes_PaymentMethods_BusinessId_PaymentMethodId" FOREIGN KEY ("BusinessId", "PaymentMethodId") REFERENCES "PaymentMethods" ("BusinessId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_ManualIncomes_Users_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE TABLE "Payments" (
        "Id" uuid NOT NULL,
        "BusinessId" uuid NOT NULL,
        "RequestId" uuid NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        "Amount" numeric(9,2) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "ActorUserId" uuid NOT NULL,
        "Description" character varying(1000),
        "Reference" character varying(200),
        "CorrectsId" uuid,
        "ReversesId" uuid,
        "CorrectionReason" character varying(500),
        "WorkOrderId" uuid NOT NULL,
        "PaymentMethodId" uuid NOT NULL,
        "PaymentMethodName" character varying(100) NOT NULL,
        CONSTRAINT "PK_Payments" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_Payments_BusinessId_Id" UNIQUE ("BusinessId", "Id"),
        CONSTRAINT "CK_Payments_Id" CHECK ("Id" <> '00000000-0000-0000-0000-000000000000'::uuid),
        CONSTRAINT "CK_Payments_Timestamps" CHECK ("UpdatedAt" >= "CreatedAt"),
        CONSTRAINT "CK_Payments_Values" CHECK ("Amount" BETWEEN 0.01 AND 9999999.99 AND "Currency" = 'TRY' AND "RequestHash" ~ '^[0-9a-f]{64}$' AND "RequestId" <> '00000000-0000-0000-0000-000000000000'::uuid AND (("ReversesId" IS NULL AND "CorrectionReason" IS NULL) OR ("ReversesId" IS NOT NULL AND "ReversesId" <> "Id" AND length(btrim("CorrectionReason")) > 0))),
        CONSTRAINT "FK_Payments_Businesses_BusinessId" FOREIGN KEY ("BusinessId") REFERENCES "Businesses" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Payments_PaymentMethods_BusinessId_PaymentMethodId" FOREIGN KEY ("BusinessId", "PaymentMethodId") REFERENCES "PaymentMethods" ("BusinessId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Payments_Payments_BusinessId_CorrectsId" FOREIGN KEY ("BusinessId", "CorrectsId") REFERENCES "Payments" ("BusinessId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Payments_Payments_BusinessId_ReversesId" FOREIGN KEY ("BusinessId", "ReversesId") REFERENCES "Payments" ("BusinessId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Payments_Users_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Payments_WorkOrders_BusinessId_WorkOrderId" FOREIGN KEY ("BusinessId", "WorkOrderId") REFERENCES "WorkOrders" ("BusinessId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE INDEX "IX_Expenses_ActorUserId" ON "Expenses" ("ActorUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE INDEX "IX_Expenses_BusinessId_CorrectsId" ON "Expenses" ("BusinessId", "CorrectsId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE INDEX "IX_Expenses_BusinessId_EmployeeMembershipId" ON "Expenses" ("BusinessId", "EmployeeMembershipId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE INDEX "IX_Expenses_BusinessId_OccurredAtUtc_Id" ON "Expenses" ("BusinessId", "OccurredAtUtc", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE UNIQUE INDEX "IX_Expenses_BusinessId_RequestId" ON "Expenses" ("BusinessId", "RequestId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE UNIQUE INDEX "IX_Expenses_BusinessId_ReversesId" ON "Expenses" ("BusinessId", "ReversesId") WHERE "ReversesId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE INDEX "IX_ManualIncomes_ActorUserId" ON "ManualIncomes" ("ActorUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE INDEX "IX_ManualIncomes_BusinessId_CorrectsId" ON "ManualIncomes" ("BusinessId", "CorrectsId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE INDEX "IX_ManualIncomes_BusinessId_OccurredAtUtc_Id" ON "ManualIncomes" ("BusinessId", "OccurredAtUtc", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE INDEX "IX_ManualIncomes_BusinessId_PaymentMethodId" ON "ManualIncomes" ("BusinessId", "PaymentMethodId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE UNIQUE INDEX "IX_ManualIncomes_BusinessId_RequestId" ON "ManualIncomes" ("BusinessId", "RequestId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE UNIQUE INDEX "IX_ManualIncomes_BusinessId_ReversesId" ON "ManualIncomes" ("BusinessId", "ReversesId") WHERE "ReversesId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE INDEX "IX_PaymentMethods_ActorUserId" ON "PaymentMethods" ("ActorUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE UNIQUE INDEX "IX_PaymentMethods_BusinessId_Name" ON "PaymentMethods" ("BusinessId", "Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE INDEX "IX_Payments_ActorUserId" ON "Payments" ("ActorUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE INDEX "IX_Payments_BusinessId_CorrectsId" ON "Payments" ("BusinessId", "CorrectsId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE INDEX "IX_Payments_BusinessId_OccurredAtUtc_Id" ON "Payments" ("BusinessId", "OccurredAtUtc", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE INDEX "IX_Payments_BusinessId_PaymentMethodId" ON "Payments" ("BusinessId", "PaymentMethodId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE UNIQUE INDEX "IX_Payments_BusinessId_RequestId" ON "Payments" ("BusinessId", "RequestId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE UNIQUE INDEX "IX_Payments_BusinessId_ReversesId" ON "Payments" ("BusinessId", "ReversesId") WHERE "ReversesId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    CREATE INDEX "IX_Payments_BusinessId_WorkOrderId" ON "Payments" ("BusinessId", "WorkOrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125132208_OwnerFinancialLedger') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261125132208_OwnerFinancialLedger', '10.0.13');
    END IF;
END $EF$;
COMMIT;
