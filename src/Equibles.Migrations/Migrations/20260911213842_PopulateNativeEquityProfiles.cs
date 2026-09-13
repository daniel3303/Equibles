using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class PopulateNativeEquityProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cusip",
                table: "EquitySecurity",
                type: "character varying(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MarketCapitalization",
                table: "EquitySecurity",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationTitle",
                table: "EquitySecurity",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RegistrationType",
                table: "EquitySecurity",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "SharesOutstanding",
                table: "EquitySecurity",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "HistoricalCusipBackfillAmbiguous",
                table: "EquityListing",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "HistoricalCusipBackfillCandidateOn",
                table: "EquityListing",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "HistoricalCusipBackfillCandidates",
                table: "EquityListing",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HistoricalCusipBackfillRequestedAt",
                table: "EquityListing",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HistoricalCusipBackfillSweepStartedAt",
                table: "EquityListing",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HistoricalPriceBackfillAttemptedAt",
                table: "EquityListing",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDirectoryListed",
                table: "EquityListing",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsReferenceListed",
                table: "EquityListing",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PriceHistoryBackfilled",
                table: "EquityListing",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "YahooEnrichmentAttemptedAt",
                table: "EquityListing",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cik",
                table: "EquityIssuer",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "EquityIssuer",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntityType",
                table: "EquityIssuer",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FiscalYearEndDay",
                table: "EquityIssuer",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FiscalYearEndMonth",
                table: "EquityIssuer",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IndustryId",
                table: "EquityIssuer",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "SecondaryCiks",
                table: "EquityIssuer",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sic",
                table: "EquityIssuer",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "EquityIssuer",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WebsiteCheckedAt",
                table: "EquityIssuer",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EquityIssuerPresentation",
                columns: table => new
                {
                    EquityIssuerId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquityListingId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquityIssuerPresentation", x => x.EquityIssuerId);
                    table.ForeignKey(
                        name: "FK_EquityIssuerPresentation_EquityIssuer_EquityIssuerId",
                        column: x => x.EquityIssuerId,
                        principalTable: "EquityIssuer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EquityIssuerPresentation_EquityListing_EquityListingId",
                        column: x => x.EquityListingId,
                        principalTable: "EquityListing",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquityIssuer_Cik",
                table: "EquityIssuer",
                column: "Cik",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquityIssuer_IndustryId",
                table: "EquityIssuer",
                column: "IndustryId");

            migrationBuilder.CreateIndex(
                name: "IX_EquityIssuerPresentation_EquityListingId",
                table: "EquityIssuerPresentation",
                column: "EquityListingId");

            migrationBuilder.AddForeignKey(
                name: "FK_EquityIssuer_Industry_IndustryId",
                table: "EquityIssuer",
                column: "IndustryId",
                principalTable: "Industry",
                principalColumn: "Id");
            migrationBuilder.Sql(NativeProfilesSql);
            migrationBuilder.Sql("""
                -- Every presentation must select a listing owned by the same issuer.
                -- Locks also prevent a concurrent security/listing reassignment from invalidating a checked selection.
                CREATE FUNCTION eq_check_issuer_presentation() RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE
                    row_to_check record;
                BEGIN
                    IF TG_TABLE_NAME = 'EquityIssuerPresentation' THEN
                        FOR row_to_check IN
                            SELECT p."EquityIssuerId" AS expected_owner, s."EquityIssuerId" AS actual_owner
                            FROM "EquityIssuerPresentation" p
                            JOIN "EquityListing" l ON l."Id" = p."EquityListingId"
                            JOIN "EquitySecurity" s ON s."Id" = l."EquitySecurityId"
                            WHERE p."EquityIssuerId" = NEW."EquityIssuerId"
                            FOR SHARE OF l, s
                        LOOP
                            IF row_to_check.expected_owner <> row_to_check.actual_owner THEN
                                RAISE EXCEPTION 'Issuer presentation cannot select another issuer''s listing' USING ERRCODE = '23514';
                            END IF;
                        END LOOP;
                    ELSE
                        FOR row_to_check IN
                            SELECT p."EquityIssuerId" AS expected_owner, s."EquityIssuerId" AS actual_owner
                            FROM "EquityIssuerPresentation" p
                            JOIN "EquityListing" l ON l."Id" = p."EquityListingId"
                            JOIN "EquitySecurity" s ON s."Id" = l."EquitySecurityId"
                            WHERE (TG_TABLE_NAME = 'EquityListing' AND l."Id" = NEW."Id")
                               OR (TG_TABLE_NAME = 'EquitySecurity' AND s."Id" = NEW."Id")
                            FOR SHARE OF l, s
                        LOOP
                            IF row_to_check.expected_owner <> row_to_check.actual_owner THEN
                                RAISE EXCEPTION 'Identity reassignment would invalidate an issuer presentation' USING ERRCODE = '23514';
                            END IF;
                        END LOOP;
                    END IF;
                    RETURN NEW;
                END;
                $body$;
                CREATE CONSTRAINT TRIGGER equity_presentation_owner_check
                AFTER INSERT OR UPDATE ON "EquityIssuerPresentation" DEFERRABLE INITIALLY IMMEDIATE
                FOR EACH ROW EXECUTE FUNCTION eq_check_issuer_presentation();
                CREATE CONSTRAINT TRIGGER equity_presentation_listing_check
                AFTER UPDATE OF "EquitySecurityId" ON "EquityListing" DEFERRABLE INITIALLY IMMEDIATE
                FOR EACH ROW EXECUTE FUNCTION eq_check_issuer_presentation();
                CREATE CONSTRAINT TRIGGER equity_presentation_security_check
                AFTER UPDATE OF "EquityIssuerId" ON "EquitySecurity" DEFERRABLE INITIALLY IMMEDIATE
                FOR EACH ROW EXECUTE FUNCTION eq_check_issuer_presentation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Native profiles retain source data; revert binaries without dropping migrated records.");
        }

        private const string NativeProfilesSql = """
CREATE OR REPLACE FUNCTION public.eq_refresh_native_profile(owner_id uuid)
RETURNS void LANGUAGE plpgsql AS $fn$
DECLARE owner_row "CommonStock"%ROWTYPE; issuer_id uuid; primary_listing_id uuid;
BEGIN
    SELECT * INTO owner_row FROM "CommonStock" WHERE "Id" = owner_id;
    IF NOT FOUND THEN RETURN; END IF;
    SELECT "Id" INTO issuer_id FROM "EquityIssuer" WHERE "CommonStockId" = owner_id;
    IF issuer_id IS NULL THEN RAISE EXCEPTION 'Missing issuer identity for %', owner_id; END IF;
    UPDATE "EquityIssuer" SET
      "Description" = owner_row."Description", "Cik" = owner_row."Cik",
      "SecondaryCiks" = owner_row."SecondaryCiks", "Website" = owner_row."Website",
      "WebsiteCheckedAt" = owner_row."WebsiteCheckedAt", "FiscalYearEndMonth" = owner_row."FiscalYearEndMonth",
      "FiscalYearEndDay" = owner_row."FiscalYearEndDay", "Sic" = owner_row."Sic",
      "EntityType" = owner_row."EntityType", "IndustryId" = owner_row."IndustryId"
    WHERE "Id" = issuer_id;

    SELECT "EquityListingId" INTO primary_listing_id FROM "LegacyEquityListing"
      WHERE "CommonStockId" = owner_id AND "ListedTicker" = owner_row."Ticker";
    UPDATE "EquityListing" listing SET
      "IsDirectoryListed" = COALESCE(listing."Ticker" = owner_row."Ticker" OR listing."Ticker" = ANY(owner_row."SecondaryTickers"), false),
      "IsReferenceListed" = COALESCE(listing."Ticker" = ANY(owner_row."ReferenceTickers"), false),
      "PriceHistoryBackfilled" = COALESCE(listing."Ticker" = ANY(owner_row."PriceHistoryBackfilledTickers"), false)
    FROM "LegacyEquityListing" source
    WHERE source."CommonStockId" = owner_id AND source."EquityListingId" = listing."Id";
    IF primary_listing_id IS NULL THEN RETURN; END IF;

    UPDATE "EquitySecurity" security SET
      "Cusip" = owner_row."Cusip", "RegistrationType" = owner_row."ListedSecurityType",
      "RegistrationTitle" = owner_row."ListedSecurityTitle", "SharesOutstanding" = owner_row."SharesOutStanding",
      "MarketCapitalization" = owner_row."MarketCapitalization"
    FROM "EquityListing" listing
    WHERE listing."Id" = primary_listing_id AND security."Id" = listing."EquitySecurityId";
    UPDATE "EquityListing" SET
      "DelistedOn" = CASE WHEN "IdentityState" = 0 THEN owner_row."DelistedOn" ELSE "DelistedOn" END, "YahooEnrichmentAttemptedAt" = owner_row."YahooEnrichmentAttemptedAt",
      "HistoricalPriceBackfillAttemptedAt" = owner_row."HistoricalPriceBackfillAttemptedAt",
      "HistoricalCusipBackfillRequestedAt" = owner_row."HistoricalCusipBackfillRequestedAt",
      "HistoricalCusipBackfillCandidates" = owner_row."HistoricalCusipBackfillCandidates",
      "HistoricalCusipBackfillCandidateOn" = owner_row."HistoricalCusipBackfillCandidateOn",
      "HistoricalCusipBackfillAmbiguous" = owner_row."HistoricalCusipBackfillAmbiguous",
      "HistoricalCusipBackfillSweepStartedAt" = owner_row."HistoricalCusipBackfillSweepStartedAt"
    WHERE "Id" = primary_listing_id;
    INSERT INTO "EquityIssuerPresentation" ("EquityIssuerId", "EquityListingId")
      VALUES (issuer_id, primary_listing_id)
      ON CONFLICT ("EquityIssuerId") DO UPDATE SET "EquityListingId" = EXCLUDED."EquityListingId";
END $fn$;

CREATE OR REPLACE FUNCTION public.eq_sync_native_profile()
RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
    PERFORM public.eq_refresh_native_profile(NEW."Id");
    RETURN NEW;
END $fn$;

CREATE TRIGGER equity_identity_native_profiles_write
AFTER INSERT OR UPDATE ON "CommonStock"
FOR EACH ROW EXECUTE FUNCTION public.eq_sync_native_profile();

SELECT public.eq_refresh_native_profile("Id") FROM "CommonStock";
""";
    }
}
