using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AttributeCorporateActionsToListings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockSplit_CommonStockId_EffectiveDate",
                table: "StockSplit");

            migrationBuilder.DropIndex(
                name: "IX_StockSplit_CommonStockId_PriceSeriesTicker_EffectiveDate",
                table: "StockSplit");

            migrationBuilder.DropIndex(
                name: "IX_CashDividend_CommonStockId_ExDate",
                table: "CashDividend");

            migrationBuilder.AddColumn<Guid>(
                name: "EquityListingId",
                table: "StockSplit",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "CashDividend",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EquityListingId",
                table: "CashDividend",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockSplit_CommonStockId_EffectiveDate",
                table: "StockSplit",
                columns: new[] { "CommonStockId", "EffectiveDate" },
                unique: true,
                filter: "\"PriceSeriesTicker\" IS NULL AND \"EquityListingId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockSplit_CommonStockId_PriceSeriesTicker_EffectiveDate",
                table: "StockSplit",
                columns: new[] { "CommonStockId", "PriceSeriesTicker", "EffectiveDate" },
                unique: true,
                filter: "\"PriceSeriesTicker\" IS NOT NULL AND \"EquityListingId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockSplit_EquityListingId_EffectiveDate",
                table: "StockSplit",
                columns: new[] { "EquityListingId", "EffectiveDate" },
                unique: true,
                filter: "\"EquityListingId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashDividend_CommonStockId_ExDate",
                table: "CashDividend",
                columns: new[] { "CommonStockId", "ExDate" },
                unique: true,
                filter: "\"EquityListingId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashDividend_EquityListingId_ExDate",
                table: "CashDividend",
                columns: new[] { "EquityListingId", "ExDate" },
                unique: true,
                filter: "\"EquityListingId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_CashDividend_EquityListing_EquityListingId",
                table: "CashDividend",
                column: "EquityListingId",
                principalTable: "EquityListing",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockSplit_EquityListing_EquityListingId",
                table: "StockSplit",
                column: "EquityListingId",
                principalTable: "EquityListing",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION eq_validate_corporate_action_listing() RETURNS trigger
                LANGUAGE plpgsql AS $body$
                DECLARE owner_id uuid; source_ticker text; denomination text;
                BEGIN
                    IF TG_OP = 'UPDATE' AND OLD."EquityListingId" IS NOT NULL AND NEW."EquityListingId" IS DISTINCT FROM OLD."EquityListingId" THEN
                        RAISE EXCEPTION 'A recorded corporate action listing cannot be changed';
                    END IF;
                    IF NEW."EquityListingId" IS NULL THEN RETURN NEW; END IF;
                    SELECT s."EquityIssuerId", l."Ticker", l."TradingCurrency"
                    INTO owner_id, source_ticker, denomination
                    FROM "EquityListing" l JOIN "EquitySecurity" s ON s."Id" = l."EquitySecurityId"
                    WHERE l."Id" = NEW."EquityListingId" FOR SHARE OF l, s;
                    IF owner_id IS DISTINCT FROM NEW."CommonStockId" THEN
                        RAISE EXCEPTION 'Corporate action listing belongs to another issuer';
                    END IF;
                    IF TG_TABLE_NAME = 'StockSplit' THEN
                        IF NEW."PriceSeriesTicker" IS NULL OR (NEW."PriceSeriesTicker" <> source_ticker AND NOT EXISTS (
                            SELECT 1 FROM "EquityListingTickerAlias" a
                            WHERE a."EquityListingId" = NEW."EquityListingId" AND a."Ticker" = NEW."PriceSeriesTicker"
                        )) THEN RAISE EXCEPTION 'Split source symbol does not identify its recorded listing'; END IF;
                    ELSE
                        IF NEW."Currency" IS NULL OR NEW."Currency" IS DISTINCT FROM denomination THEN
                            RAISE EXCEPTION 'Dividend denomination does not match its recorded listing';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $body$;
                CREATE TRIGGER equity_split_listing_owner BEFORE INSERT OR UPDATE ON "StockSplit"
                FOR EACH ROW EXECUTE FUNCTION eq_validate_corporate_action_listing();
                CREATE TRIGGER equity_dividend_listing_owner BEFORE INSERT OR UPDATE ON "CashDividend"
                FOR EACH ROW EXECUTE FUNCTION eq_validate_corporate_action_listing();
                
                CREATE OR REPLACE FUNCTION eq_preserve_corporate_action_identity() RETURNS trigger
                LANGUAGE plpgsql AS $body$
                BEGIN
                    IF TG_TABLE_NAME = 'EquityListing' THEN
                        IF NEW."EquitySecurityId" IS DISTINCT FROM OLD."EquitySecurityId" AND (
                            EXISTS (SELECT 1 FROM "StockSplit" WHERE "EquityListingId" = OLD."Id") OR
                            EXISTS (SELECT 1 FROM "CashDividend" WHERE "EquityListingId" = OLD."Id")
                        ) THEN RAISE EXCEPTION 'Listing reassignment would change recorded corporate action ownership'; END IF;
                        IF NEW."TradingCurrency" IS DISTINCT FROM OLD."TradingCurrency" AND EXISTS (
                            SELECT 1 FROM "CashDividend" WHERE "EquityListingId" = OLD."Id"
                        ) THEN RAISE EXCEPTION 'Listing denomination would invalidate recorded dividend currency'; END IF;
                    ELSE
                        IF NEW."EquityIssuerId" IS DISTINCT FROM OLD."EquityIssuerId" AND (
                            EXISTS (SELECT 1 FROM "StockSplit" a JOIN "EquityListing" l ON l."Id" = a."EquityListingId" WHERE l."EquitySecurityId" = OLD."Id") OR
                            EXISTS (SELECT 1 FROM "CashDividend" a JOIN "EquityListing" l ON l."Id" = a."EquityListingId" WHERE l."EquitySecurityId" = OLD."Id")
                        ) THEN RAISE EXCEPTION 'Security reassignment would change recorded corporate action ownership'; END IF;
                    END IF;
                    RETURN NEW;
                END;
                $body$;
                CREATE TRIGGER equity_action_listing_identity BEFORE UPDATE OF "EquitySecurityId", "TradingCurrency" ON "EquityListing"
                FOR EACH ROW EXECUTE FUNCTION eq_preserve_corporate_action_identity();
                CREATE TRIGGER equity_action_security_identity BEFORE UPDATE OF "EquityIssuerId" ON "EquitySecurity"
                FOR EACH ROW EXECUTE FUNCTION eq_preserve_corporate_action_identity();
                
                -- A source symbol can establish one U.S. listing, never a guessed presentation listing.
                -- Renamed-symbol events that collide on one listing/date stay individually unattributed.
                WITH candidates AS (
                    SELECT a."Id", a."EffectiveDate", (array_agg(DISTINCT l."Id"))[1] listing_id
                    FROM "StockSplit" a
                    JOIN "EquitySecurity" s ON s."EquityIssuerId" = a."CommonStockId"
                    JOIN "EquityListing" l ON l."EquitySecurityId" = s."Id" AND l."MarketCountryCode" = 'US'
                    WHERE a."EquityListingId" IS NULL AND a."PriceSeriesTicker" IS NOT NULL
                      AND (l."Ticker" = a."PriceSeriesTicker" OR EXISTS (
                        SELECT 1 FROM "EquityListingTickerAlias" alias
                        WHERE alias."EquityListingId" = l."Id" AND alias."Ticker" = a."PriceSeriesTicker"))
                    GROUP BY a."Id", a."EffectiveDate" HAVING count(DISTINCT l."Id") = 1
                ), unique_events AS (
                    SELECT listing_id, "EffectiveDate" FROM candidates
                    GROUP BY listing_id, "EffectiveDate" HAVING count(*) = 1
                )
                UPDATE "StockSplit" a SET "EquityListingId" = candidate.listing_id
                FROM candidates candidate JOIN unique_events event USING (listing_id, "EffectiveDate")
                WHERE a."Id" = candidate."Id" AND NOT EXISTS (
                    SELECT 1 FROM "StockSplit" known
                    WHERE known."EquityListingId" = candidate.listing_id AND known."EffectiveDate" = candidate."EffectiveDate"
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Recorded corporate action listing identity and denomination must be retained.");
        }
    }
}
