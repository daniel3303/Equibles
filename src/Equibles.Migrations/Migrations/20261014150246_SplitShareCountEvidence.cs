using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class SplitShareCountEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShareCountEvidence",
                table: "StockSplit",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ShareCountEvidenceAsOf",
                table: "StockSplit",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ShareCountEvidenceTime",
                table: "StockSplit",
                type: "timestamp with time zone",
                nullable: true);

            // A revised event definition invalidates the share-count verdict as well as the price
            // marker, for every writer; the reconciler judges the new definition on its next pass.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public."InvalidateRevisedStockSplitAdjustment"()
                 RETURNS trigger
                 LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF ROW(
                        OLD."EquityIssuerId",
                        OLD."PriceSeriesTicker",
                        OLD."EffectiveDate",
                        OLD."Numerator",
                        OLD."Denominator",
                        OLD."Source"
                    ) IS DISTINCT FROM ROW(
                        NEW."EquityIssuerId",
                        NEW."PriceSeriesTicker",
                        NEW."EffectiveDate",
                        NEW."Numerator",
                        NEW."Denominator",
                        NEW."Source"
                    ) THEN
                        NEW."PriceAdjustmentAppliedTime" := NULL;
                    END IF;
                    IF ROW(
                        OLD."EquityIssuerId",
                        OLD."EffectiveDate",
                        OLD."Numerator",
                        OLD."Denominator",
                        OLD."Source"
                    ) IS DISTINCT FROM ROW(
                        NEW."EquityIssuerId",
                        NEW."EffectiveDate",
                        NEW."Numerator",
                        NEW."Denominator",
                        NEW."Source"
                    ) THEN
                        NEW."ShareCountEvidence" := NULL;
                        NEW."ShareCountEvidenceAsOf" := NULL;
                        NEW."ShareCountEvidenceTime" := NULL;
                    END IF;
                    RETURN NEW;
                END;
                $function$;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public."InvalidateRevisedStockSplitAdjustment"()
                 RETURNS trigger
                 LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF ROW(
                        OLD."EquityIssuerId",
                        OLD."PriceSeriesTicker",
                        OLD."EffectiveDate",
                        OLD."Numerator",
                        OLD."Denominator",
                        OLD."Source"
                    ) IS DISTINCT FROM ROW(
                        NEW."EquityIssuerId",
                        NEW."PriceSeriesTicker",
                        NEW."EffectiveDate",
                        NEW."Numerator",
                        NEW."Denominator",
                        NEW."Source"
                    ) THEN
                        NEW."PriceAdjustmentAppliedTime" := NULL;
                    END IF;
                    RETURN NEW;
                END;
                $function$;
                """
            );

            migrationBuilder.DropColumn(
                name: "ShareCountEvidence",
                table: "StockSplit");

            migrationBuilder.DropColumn(
                name: "ShareCountEvidenceAsOf",
                table: "StockSplit");

            migrationBuilder.DropColumn(
                name: "ShareCountEvidenceTime",
                table: "StockSplit");
        }
    }
}
