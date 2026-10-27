START TRANSACTION;
CREATE TABLE "BusinessServices" (
    "Id" uuid NOT NULL,
    "BusinessId" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "SearchName" character varying(200) NOT NULL,
    "Description" character varying(2000),
    "Enabled" boolean NOT NULL,
    "Published" boolean NOT NULL,
    "ArchivedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_BusinessServices" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_BusinessServices_Archive" CHECK ("ArchivedAt" IS NULL OR ("ArchivedAt" >= "CreatedAt" AND NOT "Enabled" AND NOT "Published")),
    CONSTRAINT "CK_BusinessServices_Id" CHECK ("Id" <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "CK_BusinessServices_Name" CHECK (length(btrim("Name")) > 0),
    CONSTRAINT "CK_BusinessServices_Publication" CHECK (NOT "Published" OR "Enabled"),
    CONSTRAINT "CK_BusinessServices_SearchName" CHECK (length(btrim("SearchName")) > 0),
    CONSTRAINT "CK_BusinessServices_Timestamps" CHECK ("UpdatedAt" >= "CreatedAt"),
    CONSTRAINT "FK_BusinessServices_Businesses_BusinessId" FOREIGN KEY ("BusinessId") REFERENCES "Businesses" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "ServiceOfferings" (
    "ServiceId" uuid NOT NULL,
    "Category" character varying(32) NOT NULL,
    "Price" numeric(9,2) NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "ActiveMinutes" integer NOT NULL,
    "WaitingMinutes" integer,
    "Employees" integer NOT NULL,
    CONSTRAINT "PK_ServiceOfferings" PRIMARY KEY ("ServiceId", "Category"),
    CONSTRAINT "CK_ServiceOfferings_Category" CHECK ("Category" IN ('Car','Suv','Van','Pickup','Motorcycle','Other')),
    CONSTRAINT "CK_ServiceOfferings_Currency" CHECK ("Currency" = 'TRY'),
    CONSTRAINT "CK_ServiceOfferings_Estimates" CHECK ("ActiveMinutes" BETWEEN 1 AND 10080 AND ("WaitingMinutes" IS NULL OR "WaitingMinutes" BETWEEN 0 AND 43200) AND "Employees" BETWEEN 1 AND 100),
    CONSTRAINT "CK_ServiceOfferings_Price" CHECK ("Price" BETWEEN 0 AND 9999999.99 AND scale("Price") <= 2),
    CONSTRAINT "FK_ServiceOfferings_BusinessServices_ServiceId" FOREIGN KEY ("ServiceId") REFERENCES "BusinessServices" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_BusinessServices_BusinessId_SearchName_Id" ON "BusinessServices" ("BusinessId", "SearchName", "Id");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261027083159_ServiceCatalogAndOfferings', '10.0.13');

COMMIT;
