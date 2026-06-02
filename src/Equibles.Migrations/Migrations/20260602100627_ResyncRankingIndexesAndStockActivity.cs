using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ResyncRankingIndexesAndStockActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InsiderTransaction_TransactionDate",
                table: "InsiderTransaction"
            );

            migrationBuilder.CreateTable(
                name: "StockQuarterlyActivity",
                columns: table => new
                {
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PreviousReportDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CurrentShares = table.Column<long>(type: "bigint", nullable: false),
                    PreviousShares = table.Column<long>(type: "bigint", nullable: false),
                    CurrentValue = table.Column<long>(type: "bigint", nullable: false),
                    PreviousValue = table.Column<long>(type: "bigint", nullable: false),
                    CurrentFilerCount = table.Column<int>(type: "integer", nullable: false),
                    PreviousFilerCount = table.Column<int>(type: "integer", nullable: false),
                    NewFilerCount = table.Column<int>(type: "integer", nullable: false),
                    SoldOutFilerCount = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_StockQuarterlyActivity",
                        x => new { x.CommonStockId, x.ReportDate }
                    );
                }
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_InstitutionalHolding_ReportDate_CommonStockId_Institutional~",
                    table: "InstitutionalHolding",
                    columns: new[] { "ReportDate", "CommonStockId", "InstitutionalHolderId" }
                )
                .Annotation("Npgsql:IndexInclude", new[] { "Shares", "Value" });

            migrationBuilder
                .CreateIndex(
                    name: "IX_InstitutionalHolding_ReportDate_InstitutionalHolderId_Commo~",
                    table: "InstitutionalHolding",
                    columns: new[] { "ReportDate", "InstitutionalHolderId", "CommonStockId" }
                )
                .Annotation("Npgsql:IndexInclude", new[] { "Shares", "Value" });

            migrationBuilder
                .CreateIndex(
                    name: "IX_InsiderTransaction_TransactionDate",
                    table: "InsiderTransaction",
                    column: "TransactionDate"
                )
                .Annotation(
                    "Npgsql:IndexInclude",
                    new[]
                    {
                        "Shares",
                        "PricePerShare",
                        "IsPriceValid",
                        "SecurityKind",
                        "SecurityTitle",
                    }
                );

            migrationBuilder.CreateIndex(
                name: "IX_StockQuarterlyActivity_ReportDate",
                table: "StockQuarterlyActivity",
                column: "ReportDate"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "StockQuarterlyActivity");

            migrationBuilder.DropIndex(
                name: "IX_InstitutionalHolding_ReportDate_CommonStockId_Institutional~",
                table: "InstitutionalHolding"
            );

            migrationBuilder.DropIndex(
                name: "IX_InstitutionalHolding_ReportDate_InstitutionalHolderId_Commo~",
                table: "InstitutionalHolding"
            );

            migrationBuilder.DropIndex(
                name: "IX_InsiderTransaction_TransactionDate",
                table: "InsiderTransaction"
            );

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_TransactionDate",
                table: "InsiderTransaction",
                column: "TransactionDate"
            );
        }
    }
}
