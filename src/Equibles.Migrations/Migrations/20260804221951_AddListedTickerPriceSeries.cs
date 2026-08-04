using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddListedTickerPriceSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PriceSeriesTicker",
                table: "StockSplit",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ListedDailyStockPrice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListedTicker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Open = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    High = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Low = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Close = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AdjustedClose = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Volume = table.Column<long>(type: "bigint", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListedDailyStockPrice", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListedDailyStockPrice_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ListedDailyStockPrice_CommonStockId_ListedTicker_Date",
                table: "ListedDailyStockPrice",
                columns: new[] { "CommonStockId", "ListedTicker", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListedDailyStockPrice_Date",
                table: "ListedDailyStockPrice",
                column: "Date");

            migrationBuilder.Sql(
                """
                CREATE FUNCTION "InvalidateRevisedStockSplitAdjustment"()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF ROW(
                        OLD."CommonStockId",
                        OLD."PriceSeriesTicker",
                        OLD."EffectiveDate",
                        OLD."Numerator",
                        OLD."Denominator",
                        OLD."Source"
                    ) IS DISTINCT FROM ROW(
                        NEW."CommonStockId",
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

                CREATE TRIGGER "TR_StockSplit_InvalidateRevisedAdjustment"
                BEFORE UPDATE OF
                    "CommonStockId",
                    "PriceSeriesTicker",
                    "EffectiveDate",
                    "Numerator",
                    "Denominator",
                    "Source"
                ON "StockSplit"
                FOR EACH ROW
                EXECUTE FUNCTION "InvalidateRevisedStockSplitAdjustment"();
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "This migration cannot be downgraded safely because exact-listing price history "
                    + "may already exist in ListedDailyStockPrice. Restore from a backup instead."
            );
        }
    }
}
