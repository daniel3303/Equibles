using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddExactListedSecurityIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockSplit_CommonStockId_EffectiveDate",
                table: "StockSplit");

            migrationBuilder.DropIndex(
                name: "IX_ShortInterest_CommonStockId_SettlementDate",
                table: "ShortInterest");

            migrationBuilder.DropIndex(
                name: "IX_OffExchangeVolume_CommonStockId_WeekStartDate",
                table: "OffExchangeVolume");

            migrationBuilder.DropIndex(
                name: "IX_FailToDeliver_CommonStockId_SettlementDate",
                table: "FailToDeliver");

            migrationBuilder.DropIndex(
                name: "IX_DailyShortVolume_CommonStockId_Date",
                table: "DailyShortVolume");

            migrationBuilder.AddColumn<string>(
                name: "ListedTicker",
                table: "ShortInterest",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ListedTicker",
                table: "OffExchangeVolume",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ListedTicker",
                table: "FailToDeliver",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ListedTicker",
                table: "DailyShortVolume",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE "ShortInterest" AS data SET "ListedTicker" = stock."Ticker" FROM "CommonStock" AS stock WHERE data."CommonStockId" = stock."Id" AND data."ListedTicker" = '';
                UPDATE "OffExchangeVolume" AS data SET "ListedTicker" = stock."Ticker" FROM "CommonStock" AS stock WHERE data."CommonStockId" = stock."Id" AND data."ListedTicker" = '';
                UPDATE "FailToDeliver" AS data SET "ListedTicker" = stock."Ticker" FROM "CommonStock" AS stock WHERE data."CommonStockId" = stock."Id" AND data."ListedTicker" = '';
                UPDATE "DailyShortVolume" AS data SET "ListedTicker" = stock."Ticker" FROM "CommonStock" AS stock WHERE data."CommonStockId" = stock."Id" AND data."ListedTicker" = '';
                UPDATE "CommonStock" SET "ReferenceTickers" = ARRAY[]::text[] WHERE "ReferenceTickers" IS NULL;
                """
            );

            migrationBuilder.AlterColumn<List<string>>(
                name: "ReferenceTickers",
                table: "CommonStock",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]",
                oldClrType: typeof(List<string>),
                oldType: "text[]",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockSplit_CommonStockId_EffectiveDate",
                table: "StockSplit",
                columns: new[] { "CommonStockId", "EffectiveDate" },
                unique: true,
                filter: "\"PriceSeriesTicker\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockSplit_CommonStockId_PriceSeriesTicker_EffectiveDate",
                table: "StockSplit",
                columns: new[] { "CommonStockId", "PriceSeriesTicker", "EffectiveDate" },
                unique: true,
                filter: "\"PriceSeriesTicker\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ShortInterest_CommonStockId_ListedTicker_SettlementDate",
                table: "ShortInterest",
                columns: new[] { "CommonStockId", "ListedTicker", "SettlementDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OffExchangeVolume_CommonStockId_ListedTicker_WeekStartDate",
                table: "OffExchangeVolume",
                columns: new[] { "CommonStockId", "ListedTicker", "WeekStartDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FailToDeliver_CommonStockId_ListedTicker_SettlementDate",
                table: "FailToDeliver",
                columns: new[] { "CommonStockId", "ListedTicker", "SettlementDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyShortVolume_CommonStockId_ListedTicker_Date",
                table: "DailyShortVolume",
                columns: new[] { "CommonStockId", "ListedTicker", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "StockSplit"
                        GROUP BY "CommonStockId", "EffectiveDate"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot roll back exact listed-security split identity while multiple listing splits share one filer and effective date.';
                    END IF;
                END $$;
                """
            );

            migrationBuilder.DropIndex(
                name: "IX_StockSplit_CommonStockId_EffectiveDate",
                table: "StockSplit");

            migrationBuilder.DropIndex(
                name: "IX_StockSplit_CommonStockId_PriceSeriesTicker_EffectiveDate",
                table: "StockSplit");

            migrationBuilder.DropIndex(
                name: "IX_ShortInterest_CommonStockId_ListedTicker_SettlementDate",
                table: "ShortInterest");

            migrationBuilder.DropIndex(
                name: "IX_OffExchangeVolume_CommonStockId_ListedTicker_WeekStartDate",
                table: "OffExchangeVolume");

            migrationBuilder.DropIndex(
                name: "IX_FailToDeliver_CommonStockId_ListedTicker_SettlementDate",
                table: "FailToDeliver");

            migrationBuilder.DropIndex(
                name: "IX_DailyShortVolume_CommonStockId_ListedTicker_Date",
                table: "DailyShortVolume");

            migrationBuilder.Sql(
                """
                DELETE FROM "ShortInterest" AS data USING "CommonStock" AS stock WHERE data."CommonStockId" = stock."Id" AND data."ListedTicker" <> stock."Ticker";
                DELETE FROM "OffExchangeVolume" AS data USING "CommonStock" AS stock WHERE data."CommonStockId" = stock."Id" AND data."ListedTicker" <> stock."Ticker";
                DELETE FROM "FailToDeliver" AS data USING "CommonStock" AS stock WHERE data."CommonStockId" = stock."Id" AND data."ListedTicker" <> stock."Ticker";
                DELETE FROM "DailyShortVolume" AS data USING "CommonStock" AS stock WHERE data."CommonStockId" = stock."Id" AND data."ListedTicker" <> stock."Ticker";
                """
            );

            migrationBuilder.DropColumn(
                name: "ListedTicker",
                table: "ShortInterest");

            migrationBuilder.DropColumn(
                name: "ListedTicker",
                table: "OffExchangeVolume");

            migrationBuilder.DropColumn(
                name: "ListedTicker",
                table: "FailToDeliver");

            migrationBuilder.DropColumn(
                name: "ListedTicker",
                table: "DailyShortVolume");

            migrationBuilder.AlterColumn<List<string>>(
                name: "ReferenceTickers",
                table: "CommonStock",
                type: "text[]",
                nullable: true,
                oldClrType: typeof(List<string>),
                oldType: "text[]",
                oldDefaultValueSql: "'{}'::text[]");

            migrationBuilder.CreateIndex(
                name: "IX_StockSplit_CommonStockId_EffectiveDate",
                table: "StockSplit",
                columns: new[] { "CommonStockId", "EffectiveDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShortInterest_CommonStockId_SettlementDate",
                table: "ShortInterest",
                columns: new[] { "CommonStockId", "SettlementDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OffExchangeVolume_CommonStockId_WeekStartDate",
                table: "OffExchangeVolume",
                columns: new[] { "CommonStockId", "WeekStartDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FailToDeliver_CommonStockId_SettlementDate",
                table: "FailToDeliver",
                columns: new[] { "CommonStockId", "SettlementDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyShortVolume_CommonStockId_Date",
                table: "DailyShortVolume",
                columns: new[] { "CommonStockId", "Date" },
                unique: true);
        }
    }
}
