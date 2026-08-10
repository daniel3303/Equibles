using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileDividendAdjustedClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PriceAdjustmentAppliedAmountPerShare",
                table: "CashDividend",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PriceAdjustmentAppliedTime",
                table: "CashDividend",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CorporateActionPriceReconciliationCursor",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastCommonStockId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastListedTicker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorporateActionPriceReconciliationCursor", x => x.Name);
                });

            migrationBuilder.InsertData(
                table: "CorporateActionPriceReconciliationCursor",
                columns: new[] { "Name", "LastCommonStockId", "LastListedTicker", "UpdatedAt" },
                values: new object[] { "CorporateActions.PriceReconciliation", null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_CashDividend_PriceAdjustmentAppliedTime",
                table: "CashDividend",
                column: "PriceAdjustmentAppliedTime");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorporateActionPriceReconciliationCursor");

            migrationBuilder.DropIndex(
                name: "IX_CashDividend_PriceAdjustmentAppliedTime",
                table: "CashDividend");

            migrationBuilder.DropColumn(
                name: "PriceAdjustmentAppliedAmountPerShare",
                table: "CashDividend");

            migrationBuilder.DropColumn(
                name: "PriceAdjustmentAppliedTime",
                table: "CashDividend");
        }
    }
}
