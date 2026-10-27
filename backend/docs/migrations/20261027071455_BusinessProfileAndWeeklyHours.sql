START TRANSACTION;
ALTER TABLE "Businesses" ADD "AddressLine1" character varying(250);

ALTER TABLE "Businesses" ADD "AddressLine2" character varying(250);

ALTER TABLE "Businesses" ADD "City" character varying(100);

ALTER TABLE "Businesses" ADD "ContactEmail" character varying(320);

ALTER TABLE "Businesses" ADD "CountryCode" character varying(2);

ALTER TABLE "Businesses" ADD "Description" character varying(2000);

ALTER TABLE "Businesses" ADD "District" character varying(100);

ALTER TABLE "Businesses" ADD "HoursConfigured" boolean NOT NULL DEFAULT FALSE;

ALTER TABLE "Businesses" ADD "Phone" character varying(40);

ALTER TABLE "Businesses" ADD "PostalCode" character varying(32);

ALTER TABLE "Businesses" ADD "Region" character varying(100);

ALTER TABLE "Businesses" ADD "TimeZoneId" character varying(100);

CREATE TABLE "WeeklyOpeningDays" (
    "BusinessId" uuid NOT NULL,
    "Day" integer NOT NULL,
    "Closed" boolean NOT NULL,
    "OpensAtMinute" integer,
    "ClosesAtMinute" integer,
    CONSTRAINT "PK_WeeklyOpeningDays" PRIMARY KEY ("BusinessId", "Day"),
    CONSTRAINT "CK_WeeklyOpeningDays_Day" CHECK ("Day" BETWEEN 1 AND 7),
    CONSTRAINT "CK_WeeklyOpeningDays_Interval" CHECK (("Closed" AND "OpensAtMinute" IS NULL AND "ClosesAtMinute" IS NULL) OR (NOT "Closed" AND "OpensAtMinute" IS NOT NULL AND "ClosesAtMinute" IS NOT NULL AND "OpensAtMinute" >= 0 AND "OpensAtMinute" < "ClosesAtMinute" AND "ClosesAtMinute" <= 1440)),
    CONSTRAINT "FK_WeeklyOpeningDays_Businesses_BusinessId" FOREIGN KEY ("BusinessId") REFERENCES "Businesses" ("Id") ON DELETE RESTRICT
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261027071455_BusinessProfileAndWeeklyHours', '10.0.13');

COMMIT;
