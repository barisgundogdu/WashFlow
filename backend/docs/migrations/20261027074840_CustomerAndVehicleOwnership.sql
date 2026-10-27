START TRANSACTION;
CREATE TABLE "LocalCustomers" (
    "Id" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "BusinessId" uuid NOT NULL,
    "FullName" character varying(200) NOT NULL,
    "NormalizedName" character varying(200) NOT NULL,
    "Phone" character varying(40),
    "NormalizedPhone" character varying(40),
    "Email" character varying(320),
    "Notes" character varying(2000),
    CONSTRAINT "PK_LocalCustomers" PRIMARY KEY ("Id"),
    CONSTRAINT "AK_LocalCustomers_BusinessId_Id" UNIQUE ("BusinessId", "Id"),
    CONSTRAINT "CK_LocalCustomers_FullName" CHECK (length(btrim("FullName")) > 0),
    CONSTRAINT "CK_LocalCustomers_Id" CHECK ("Id" <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "CK_LocalCustomers_NormalizedName" CHECK (length(btrim("NormalizedName")) > 0),
    CONSTRAINT "CK_LocalCustomers_Timestamps" CHECK ("UpdatedAt" >= "CreatedAt"),
    CONSTRAINT "FK_LocalCustomers_Businesses_BusinessId" FOREIGN KEY ("BusinessId") REFERENCES "Businesses" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "OwnedVehicles" (
    "Id" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "CustomerProfileId" uuid NOT NULL,
    "Plate" character varying(32) NOT NULL,
    "NormalizedPlate" character varying(64) NOT NULL,
    "Brand" character varying(80) NOT NULL,
    "Model" character varying(100) NOT NULL,
    "Category" character varying(32) NOT NULL,
    "ModelYear" integer,
    "Color" character varying(60),
    "Notes" character varying(2000),
    "ArchivedAt" timestamp with time zone,
    CONSTRAINT "PK_OwnedVehicles" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_OwnedVehicles_Archive" CHECK ("ArchivedAt" IS NULL OR "ArchivedAt" >= "CreatedAt"),
    CONSTRAINT "CK_OwnedVehicles_Brand" CHECK (length(btrim("Brand")) > 0),
    CONSTRAINT "CK_OwnedVehicles_Category" CHECK ("Category" IN ('Car','Suv','Van','Pickup','Motorcycle','Other')),
    CONSTRAINT "CK_OwnedVehicles_Id" CHECK ("Id" <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "CK_OwnedVehicles_Model" CHECK (length(btrim("Model")) > 0),
    CONSTRAINT "CK_OwnedVehicles_NormalizedPlate" CHECK (length(btrim("NormalizedPlate")) > 0),
    CONSTRAINT "CK_OwnedVehicles_Plate" CHECK (length(btrim("Plate")) > 0),
    CONSTRAINT "CK_OwnedVehicles_Timestamps" CHECK ("UpdatedAt" >= "CreatedAt"),
    CONSTRAINT "CK_OwnedVehicles_Year" CHECK ("ModelYear" IS NULL OR "ModelYear" BETWEEN 1886 AND 2100),
    CONSTRAINT "FK_OwnedVehicles_CustomerProfiles_CustomerProfileId" FOREIGN KEY ("CustomerProfileId") REFERENCES "CustomerProfiles" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "LocalVehicles" (
    "Id" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "BusinessId" uuid NOT NULL,
    "LocalCustomerId" uuid NOT NULL,
    "Plate" character varying(32) NOT NULL,
    "NormalizedPlate" character varying(64) NOT NULL,
    "Brand" character varying(80) NOT NULL,
    "Model" character varying(100) NOT NULL,
    "Category" character varying(32) NOT NULL,
    "ModelYear" integer,
    "Color" character varying(60),
    "Notes" character varying(2000),
    "ArchivedAt" timestamp with time zone,
    CONSTRAINT "PK_LocalVehicles" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_LocalVehicles_Archive" CHECK ("ArchivedAt" IS NULL OR "ArchivedAt" >= "CreatedAt"),
    CONSTRAINT "CK_LocalVehicles_Brand" CHECK (length(btrim("Brand")) > 0),
    CONSTRAINT "CK_LocalVehicles_Category" CHECK ("Category" IN ('Car','Suv','Van','Pickup','Motorcycle','Other')),
    CONSTRAINT "CK_LocalVehicles_Id" CHECK ("Id" <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "CK_LocalVehicles_Model" CHECK (length(btrim("Model")) > 0),
    CONSTRAINT "CK_LocalVehicles_NormalizedPlate" CHECK (length(btrim("NormalizedPlate")) > 0),
    CONSTRAINT "CK_LocalVehicles_Plate" CHECK (length(btrim("Plate")) > 0),
    CONSTRAINT "CK_LocalVehicles_Timestamps" CHECK ("UpdatedAt" >= "CreatedAt"),
    CONSTRAINT "CK_LocalVehicles_Year" CHECK ("ModelYear" IS NULL OR "ModelYear" BETWEEN 1886 AND 2100),
    CONSTRAINT "FK_LocalVehicles_LocalCustomers_BusinessId_LocalCustomerId" FOREIGN KEY ("BusinessId", "LocalCustomerId") REFERENCES "LocalCustomers" ("BusinessId", "Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_LocalCustomers_BusinessId_NormalizedName_Id" ON "LocalCustomers" ("BusinessId", "NormalizedName", "Id");

CREATE INDEX "IX_LocalCustomers_BusinessId_NormalizedPhone" ON "LocalCustomers" ("BusinessId", "NormalizedPhone");

CREATE INDEX "IX_LocalVehicles_BusinessId_LocalCustomerId_Id" ON "LocalVehicles" ("BusinessId", "LocalCustomerId", "Id");

CREATE INDEX "IX_LocalVehicles_BusinessId_NormalizedPlate_Id" ON "LocalVehicles" ("BusinessId", "NormalizedPlate", "Id");

CREATE INDEX "IX_OwnedVehicles_CustomerProfileId_NormalizedPlate_Id" ON "OwnedVehicles" ("CustomerProfileId", "NormalizedPlate", "Id");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261027074840_CustomerAndVehicleOwnership', '10.0.13');

COMMIT;
