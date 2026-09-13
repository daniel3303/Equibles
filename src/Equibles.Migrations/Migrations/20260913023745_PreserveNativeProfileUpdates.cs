using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.Migrations.Migrations;

public partial class PreserveNativeProfileUpdates : Migration {
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.Sql("""
-- Mixed-version writers propagate only fields or memberships they actually changed.
-- Full refresh remains limited to first insertion and the historical backfill.
CREATE OR REPLACE FUNCTION public.eq_sync_native_profile()
RETURNS trigger LANGUAGE plpgsql AS $fn$
DECLARE issuer_id uuid; primary_listing_id uuid;
BEGIN
    IF TG_OP = 'INSERT' THEN
        PERFORM public.eq_refresh_native_profile(NEW."Id");
        RETURN NEW;
    END IF;
    SELECT "Id" INTO STRICT issuer_id FROM "EquityIssuer" WHERE "CommonStockId" = NEW."Id";
    UPDATE "EquityIssuer" target SET
      "Description" = CASE WHEN NEW."Description" IS DISTINCT FROM OLD."Description" THEN NEW."Description" ELSE target."Description" END,
      "Cik" = CASE WHEN NEW."Cik" IS DISTINCT FROM OLD."Cik" THEN NEW."Cik" ELSE target."Cik" END,
      "SecondaryCiks" = CASE WHEN NEW."SecondaryCiks" IS DISTINCT FROM OLD."SecondaryCiks" THEN NEW."SecondaryCiks" ELSE target."SecondaryCiks" END,
      "Website" = CASE WHEN NEW."Website" IS DISTINCT FROM OLD."Website" THEN NEW."Website" ELSE target."Website" END,
      "WebsiteCheckedAt" = CASE WHEN NEW."WebsiteCheckedAt" IS DISTINCT FROM OLD."WebsiteCheckedAt" THEN NEW."WebsiteCheckedAt" ELSE target."WebsiteCheckedAt" END,
      "FiscalYearEndMonth" = CASE WHEN NEW."FiscalYearEndMonth" IS DISTINCT FROM OLD."FiscalYearEndMonth" THEN NEW."FiscalYearEndMonth" ELSE target."FiscalYearEndMonth" END,
      "FiscalYearEndDay" = CASE WHEN NEW."FiscalYearEndDay" IS DISTINCT FROM OLD."FiscalYearEndDay" THEN NEW."FiscalYearEndDay" ELSE target."FiscalYearEndDay" END,
      "Sic" = CASE WHEN NEW."Sic" IS DISTINCT FROM OLD."Sic" THEN NEW."Sic" ELSE target."Sic" END,
      "EntityType" = CASE WHEN NEW."EntityType" IS DISTINCT FROM OLD."EntityType" THEN NEW."EntityType" ELSE target."EntityType" END,
      "IndustryId" = CASE WHEN NEW."IndustryId" IS DISTINCT FROM OLD."IndustryId" THEN NEW."IndustryId" ELSE target."IndustryId" END
    WHERE target."Id" = issuer_id;
    UPDATE "EquityListing" target SET
      "IsDirectoryListed" = CASE WHEN COALESCE((target."Ticker" = NEW."Ticker" OR target."Ticker" = ANY(NEW."SecondaryTickers")), false) IS DISTINCT FROM COALESCE((target."Ticker" = OLD."Ticker" OR target."Ticker" = ANY(OLD."SecondaryTickers")), false) THEN COALESCE((target."Ticker" = NEW."Ticker" OR target."Ticker" = ANY(NEW."SecondaryTickers")), false) ELSE target."IsDirectoryListed" END,
      "IsReferenceListed" = CASE WHEN COALESCE((target."Ticker" = ANY(NEW."ReferenceTickers")), false) IS DISTINCT FROM COALESCE((target."Ticker" = ANY(OLD."ReferenceTickers")), false) THEN COALESCE((target."Ticker" = ANY(NEW."ReferenceTickers")), false) ELSE target."IsReferenceListed" END,
      "PriceHistoryBackfilled" = CASE WHEN COALESCE((target."Ticker" = ANY(NEW."PriceHistoryBackfilledTickers")), false) IS DISTINCT FROM COALESCE((target."Ticker" = ANY(OLD."PriceHistoryBackfilledTickers")), false) THEN COALESCE((target."Ticker" = ANY(NEW."PriceHistoryBackfilledTickers")), false) ELSE target."PriceHistoryBackfilled" END
    FROM "LegacyEquityListing" source
    WHERE source."CommonStockId" = NEW."Id" AND source."EquityListingId" = target."Id";
    SELECT "EquityListingId" INTO primary_listing_id FROM "LegacyEquityListing"
      WHERE "CommonStockId" = NEW."Id" AND "ListedTicker" = NEW."Ticker";
    IF primary_listing_id IS NULL THEN RETURN NEW; END IF;
    UPDATE "EquitySecurity" target SET
      "Cusip" = CASE WHEN NEW."Cusip" IS DISTINCT FROM OLD."Cusip" THEN NEW."Cusip" ELSE target."Cusip" END,
      "RegistrationType" = CASE WHEN NEW."ListedSecurityType" IS DISTINCT FROM OLD."ListedSecurityType" THEN NEW."ListedSecurityType" ELSE target."RegistrationType" END,
      "RegistrationTitle" = CASE WHEN NEW."ListedSecurityTitle" IS DISTINCT FROM OLD."ListedSecurityTitle" THEN NEW."ListedSecurityTitle" ELSE target."RegistrationTitle" END,
      "SharesOutstanding" = CASE WHEN NEW."SharesOutStanding" IS DISTINCT FROM OLD."SharesOutStanding" THEN NEW."SharesOutStanding" ELSE target."SharesOutstanding" END,
      "MarketCapitalization" = CASE WHEN NEW."MarketCapitalization" IS DISTINCT FROM OLD."MarketCapitalization" THEN NEW."MarketCapitalization" ELSE target."MarketCapitalization" END
    FROM "EquityListing" listing
    WHERE listing."Id" = primary_listing_id AND target."Id" = listing."EquitySecurityId";
    UPDATE "EquityListing" target SET
      "DelistedOn" = CASE WHEN target."IdentityState" = 0 AND NEW."DelistedOn" IS DISTINCT FROM OLD."DelistedOn" THEN NEW."DelistedOn" ELSE target."DelistedOn" END,
      "YahooEnrichmentAttemptedAt" = CASE WHEN NEW."YahooEnrichmentAttemptedAt" IS DISTINCT FROM OLD."YahooEnrichmentAttemptedAt" THEN NEW."YahooEnrichmentAttemptedAt" ELSE target."YahooEnrichmentAttemptedAt" END,
      "HistoricalPriceBackfillAttemptedAt" = CASE WHEN NEW."HistoricalPriceBackfillAttemptedAt" IS DISTINCT FROM OLD."HistoricalPriceBackfillAttemptedAt" THEN NEW."HistoricalPriceBackfillAttemptedAt" ELSE target."HistoricalPriceBackfillAttemptedAt" END,
      "HistoricalCusipBackfillRequestedAt" = CASE WHEN NEW."HistoricalCusipBackfillRequestedAt" IS DISTINCT FROM OLD."HistoricalCusipBackfillRequestedAt" THEN NEW."HistoricalCusipBackfillRequestedAt" ELSE target."HistoricalCusipBackfillRequestedAt" END,
      "HistoricalCusipBackfillCandidates" = CASE WHEN NEW."HistoricalCusipBackfillCandidates" IS DISTINCT FROM OLD."HistoricalCusipBackfillCandidates" THEN NEW."HistoricalCusipBackfillCandidates" ELSE target."HistoricalCusipBackfillCandidates" END,
      "HistoricalCusipBackfillCandidateOn" = CASE WHEN NEW."HistoricalCusipBackfillCandidateOn" IS DISTINCT FROM OLD."HistoricalCusipBackfillCandidateOn" THEN NEW."HistoricalCusipBackfillCandidateOn" ELSE target."HistoricalCusipBackfillCandidateOn" END,
      "HistoricalCusipBackfillAmbiguous" = CASE WHEN NEW."HistoricalCusipBackfillAmbiguous" IS DISTINCT FROM OLD."HistoricalCusipBackfillAmbiguous" THEN NEW."HistoricalCusipBackfillAmbiguous" ELSE target."HistoricalCusipBackfillAmbiguous" END,
      "HistoricalCusipBackfillSweepStartedAt" = CASE WHEN NEW."HistoricalCusipBackfillSweepStartedAt" IS DISTINCT FROM OLD."HistoricalCusipBackfillSweepStartedAt" THEN NEW."HistoricalCusipBackfillSweepStartedAt" ELSE target."HistoricalCusipBackfillSweepStartedAt" END
    WHERE target."Id" = primary_listing_id;
    IF NEW."Ticker" IS DISTINCT FROM OLD."Ticker" THEN
        INSERT INTO "EquityIssuerPresentation" ("EquityIssuerId", "EquityListingId")
          VALUES (issuer_id, primary_listing_id)
          ON CONFLICT ("EquityIssuerId") DO UPDATE SET "EquityListingId" = EXCLUDED."EquityListingId";
    END IF;
    RETURN NEW;
END $fn$;

CREATE OR REPLACE FUNCTION public.eq_sync_legacy_issuer()
RETURNS trigger LANGUAGE plpgsql AS $fn$
DECLARE symbol text;
BEGIN
    INSERT INTO "EquityIssuer" ("Id", "CommonStockId", "Name", "IdentitySourceUrl")
      VALUES (NEW."Id", NEW."Id", NEW."Name", NULL)
      ON CONFLICT ("CommonStockId") DO NOTHING;
    IF TG_OP = 'INSERT' OR NEW."Name" IS DISTINCT FROM OLD."Name" THEN
        UPDATE "EquityIssuer" SET "Name" = NEW."Name"
          WHERE "CommonStockId" = NEW."Id" AND "IdentitySourceUrl" IS NULL;
    END IF;
    FOR symbol IN SELECT DISTINCT value FROM unnest(ARRAY[NEW."Ticker"] || NEW."SecondaryTickers"
        || NEW."ReferenceTickers" || NEW."PriceHistoryBackfilledTickers") AS value
        WHERE value IS NOT NULL AND value <> '' ORDER BY value
    LOOP
        PERFORM public.eq_ensure_legacy_listing(NEW."Id", symbol);
    END LOOP;
    UPDATE "EquityListing" target SET
      "Active" = (NEW."Active" AND COALESCE((target."Ticker" = NEW."Ticker" OR target."Ticker" = ANY(NEW."SecondaryTickers") OR target."Ticker" = ANY(NEW."ReferenceTickers")), false)),
      "DelistedOn" = CASE WHEN (NEW."Active" AND COALESCE((target."Ticker" = NEW."Ticker" OR target."Ticker" = ANY(NEW."SecondaryTickers") OR target."Ticker" = ANY(NEW."ReferenceTickers")), false)) THEN NULL ELSE target."DelistedOn" END
    FROM "LegacyEquityListing" source
    WHERE source."CommonStockId" = NEW."Id" AND source."EquityListingId" = target."Id"
      AND target."IdentityState" = 0
      AND (TG_OP = 'INSERT' OR (NEW."Active" AND COALESCE((target."Ticker" = NEW."Ticker" OR target."Ticker" = ANY(NEW."SecondaryTickers") OR target."Ticker" = ANY(NEW."ReferenceTickers")), false)) IS DISTINCT FROM (OLD."Active" AND COALESCE((target."Ticker" = OLD."Ticker" OR target."Ticker" = ANY(OLD."SecondaryTickers") OR target."Ticker" = ANY(OLD."ReferenceTickers")), false)));
    RETURN NEW;
END $fn$;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Do not restore profile synchronization that overwrites native updates.");
}
