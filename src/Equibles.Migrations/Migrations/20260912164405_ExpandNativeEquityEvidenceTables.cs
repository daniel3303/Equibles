using Equibles.Migrations.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations;

public partial class ExpandNativeEquityEvidenceTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        NativeEquityEvidenceExpansion20260912.Apply(migrationBuilder,
            "CommonStockCusipAlias", "EquityIssuerCusipAlias",
            ["Id", "CreationTime", "Cusip", "EquityIssuerId"],
            """
            CREATE TABLE IF NOT EXISTS "EquityIssuerCusipAlias" (
                "Id" uuid NOT NULL,
                "EquityIssuerId" uuid NOT NULL,
                "Cusip" character varying(9) NOT NULL,
                "CreationTime" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_EquityIssuerCusipAlias" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_EquityIssuerCusipAlias_EquityIssuer_EquityIssuerId" FOREIGN KEY ("EquityIssuerId") REFERENCES "EquityIssuer" ("Id") ON DELETE RESTRICT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_EquityIssuerCusipAlias_Cusip" ON "EquityIssuerCusipAlias" ("Cusip");
            CREATE INDEX IF NOT EXISTS "IX_EquityIssuerCusipAlias_EquityIssuerId" ON "EquityIssuerCusipAlias" ("EquityIssuerId");
            """);

        NativeEquityEvidenceExpansion20260912.Apply(migrationBuilder,
            "CommonStockTickerAlias", "EquityIssuerTickerAlias",
            ["Id", "CreationTime", "EquityIssuerId", "Ticker"],
            """
            CREATE TABLE IF NOT EXISTS "EquityIssuerTickerAlias" (
                "Id" uuid NOT NULL,
                "EquityIssuerId" uuid NOT NULL,
                "Ticker" character varying(16) NOT NULL,
                "CreationTime" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_EquityIssuerTickerAlias" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_EquityIssuerTickerAlias_EquityIssuer_EquityIssuerId" FOREIGN KEY ("EquityIssuerId") REFERENCES "EquityIssuer" ("Id") ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS "IX_EquityIssuerTickerAlias_EquityIssuerId" ON "EquityIssuerTickerAlias" ("EquityIssuerId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_EquityIssuerTickerAlias_Ticker" ON "EquityIssuerTickerAlias" ("Ticker");
            """);

        NativeEquityEvidenceExpansion20260912.Apply(migrationBuilder,
            "CommonStockTickerEvidence", "EquityIssuerTickerEvidence",
            ["Id", "AccessionNumber", "EquityIssuerId", "FiledDate", "SourceDocumentId", "Ticker"],
            """
            CREATE TABLE IF NOT EXISTS "EquityIssuerTickerEvidence" (
                "Id" uuid NOT NULL,
                "EquityIssuerId" uuid NOT NULL,
                "Ticker" character varying(32) NOT NULL,
                "FiledDate" date NOT NULL,
                "SourceDocumentId" uuid NOT NULL,
                "AccessionNumber" character varying(32) NOT NULL,
                CONSTRAINT "PK_EquityIssuerTickerEvidence" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_EquityIssuerTickerEvidence_EquityIssuer_EquityIssuerId" FOREIGN KEY ("EquityIssuerId") REFERENCES "EquityIssuer" ("Id") ON DELETE RESTRICT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_EquityIssuerTickerEvidence_EquityIssuerId_Ticker_SourceDocu~" ON "EquityIssuerTickerEvidence" ("EquityIssuerId", "Ticker", "SourceDocumentId");
            CREATE INDEX IF NOT EXISTS "IX_EquityIssuerTickerEvidence_Ticker_FiledDate" ON "EquityIssuerTickerEvidence" ("Ticker", "FiledDate");
            """);

        NativeEquityEvidenceExpansion20260912.Apply(migrationBuilder,
            "CommonStockListedCusip", "EquityListingCusipEvidence",
            ["Id", "CreationTime", "Cusip", "EquityIssuerId", "ListedTicker"],
            """
            CREATE TABLE IF NOT EXISTS "EquityListingCusipEvidence" (
                "Id" uuid NOT NULL,
                "EquityIssuerId" uuid NOT NULL,
                "ListedTicker" character varying(32) NOT NULL,
                "Cusip" character varying(9) NOT NULL,
                "CreationTime" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_EquityListingCusipEvidence" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_EquityListingCusipEvidence_EquityIssuer_EquityIssuerId" FOREIGN KEY ("EquityIssuerId") REFERENCES "EquityIssuer" ("Id") ON DELETE RESTRICT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_EquityListingCusipEvidence_Cusip" ON "EquityListingCusipEvidence" ("Cusip");
            CREATE INDEX IF NOT EXISTS "IX_EquityListingCusipEvidence_EquityIssuerId" ON "EquityListingCusipEvidence" ("EquityIssuerId");
            """);

        NativeEquityEvidenceExpansion20260912.Apply(migrationBuilder,
            "CommonStockDelistedListing", "EquityListingRetirementEvidence",
            ["Id", "Cusip", "DelistedOn", "EquityIssuerId", "HistoricalCusipBackfillAmbiguous", "HistoricalCusipBackfillCandidateOn", "HistoricalCusipBackfillCandidates", "HistoricalCusipBackfillRequestedAt", "HistoricalCusipBackfillSweepStartedAt", "HistoricalPriceBackfillAttemptedAt", "ListedTicker"],
            """
            CREATE TABLE IF NOT EXISTS "EquityListingRetirementEvidence" (
                "Id" uuid NOT NULL,
                "EquityIssuerId" uuid NOT NULL,
                "ListedTicker" character varying(32) NOT NULL,
                "DelistedOn" date NOT NULL,
                "HistoricalPriceBackfillAttemptedAt" timestamp with time zone,
                "Cusip" character varying(9),
                "HistoricalCusipBackfillRequestedAt" timestamp with time zone,
                "HistoricalCusipBackfillCandidates" text[],
                "HistoricalCusipBackfillCandidateOn" date,
                "HistoricalCusipBackfillAmbiguous" boolean NOT NULL,
                "HistoricalCusipBackfillSweepStartedAt" timestamp with time zone,
                CONSTRAINT "PK_EquityListingRetirementEvidence" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_EquityListingRetirementEvidence_EquityIssuer_EquityIssuerId" FOREIGN KEY ("EquityIssuerId") REFERENCES "EquityIssuer" ("Id") ON DELETE RESTRICT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_EquityListingRetirementEvidence_EquityIssuerId_ListedTicker" ON "EquityListingRetirementEvidence" ("EquityIssuerId", "ListedTicker");
            CREATE INDEX IF NOT EXISTS "IX_EquityListingRetirementEvidence_ListedTicker_DelistedOn" ON "EquityListingRetirementEvidence" ("ListedTicker", "DelistedOn");
            """);

        NativeEquityEvidenceExpansion20260912.Apply(migrationBuilder,
            "ListedSecurity", "IssuerSecurityRegistration",
            ["Id", "AccessionNumber", "EquityIssuerId", "ExchangeName", "FiledDate", "Title", "TradingSymbol"],
            """
            CREATE TABLE IF NOT EXISTS "IssuerSecurityRegistration" (
                "Id" uuid NOT NULL,
                "EquityIssuerId" uuid NOT NULL,
                "TradingSymbol" character varying(32) NOT NULL,
                "Title" character varying(500) NOT NULL,
                "ExchangeName" character varying(100),
                "AccessionNumber" character varying(32),
                "FiledDate" date NOT NULL,
                CONSTRAINT "PK_IssuerSecurityRegistration" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_IssuerSecurityRegistration_EquityIssuer_EquityIssuerId" FOREIGN KEY ("EquityIssuerId") REFERENCES "EquityIssuer" ("Id") ON DELETE RESTRICT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_IssuerSecurityRegistration_EquityIssuerId_TradingSymbol" ON "IssuerSecurityRegistration" ("EquityIssuerId", "TradingSymbol");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Removing native equity evidence requires a verified forward migration.");
}
