using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddCongressionalTradeIssuerEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CongressionalTrade_CommonStock_CommonStockId",
                table: "CongressionalTrade");

            migrationBuilder.DropIndex(
                name: "UX_CongressionalTrade_FilingIdentity",
                table: "CongressionalTrade");

            migrationBuilder.AlterColumn<Guid>(
                name: "CommonStockId",
                table: "CongressionalTrade",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "FiledTicker",
                table: "CongressionalTrade",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "FilingKind",
                table: "CongressionalTrade",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceId",
                table: "CongressionalTrade",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceRowIndex",
                table: "CongressionalTrade",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommonStockTickerEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ticker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FiledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessionNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommonStockTickerEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommonStockTickerEvidence_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalTrade_FilingKind_SourceId_SourceRowIndex",
                table: "CongressionalTrade",
                columns: new[] { "FilingKind", "SourceId", "SourceRowIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalTrade_LegacyFilingIdentity",
                table: "CongressionalTrade",
                columns: new[] { "CommonStockId", "CongressMemberId", "TransactionDate", "TransactionType", "AssetName", "OwnerType", "AmountFrom", "AmountTo", "AssetType", "Subholding" });

            migrationBuilder.CreateIndex(
                name: "IX_CommonStockTickerEvidence_CommonStockId_Ticker_SourceDocume~",
                table: "CommonStockTickerEvidence",
                columns: new[] { "CommonStockId", "Ticker", "SourceDocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommonStockTickerEvidence_Ticker_FiledDate",
                table: "CommonStockTickerEvidence",
                columns: new[] { "Ticker", "FiledDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_CongressionalTrade_CommonStock_CommonStockId",
                table: "CongressionalTrade",
                column: "CommonStockId",
                principalTable: "CommonStock",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CongressionalTrade_CommonStock_CommonStockId",
                table: "CongressionalTrade");

            migrationBuilder.DropTable(
                name: "CommonStockTickerEvidence");

            migrationBuilder.DropIndex(
                name: "IX_CongressionalTrade_FilingKind_SourceId_SourceRowIndex",
                table: "CongressionalTrade");

            migrationBuilder.DropIndex(
                name: "IX_CongressionalTrade_LegacyFilingIdentity",
                table: "CongressionalTrade");

            migrationBuilder.DropColumn(
                name: "FiledTicker",
                table: "CongressionalTrade");

            migrationBuilder.DropColumn(
                name: "FilingKind",
                table: "CongressionalTrade");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "CongressionalTrade");

            migrationBuilder.DropColumn(
                name: "SourceRowIndex",
                table: "CongressionalTrade");

            migrationBuilder.Sql(
                """
                DELETE FROM "CongressionalTrade"
                WHERE "CommonStockId" IS NULL;

                WITH ranked AS (
                    SELECT
                        "Id",
                        ROW_NUMBER() OVER (
                            PARTITION BY
                                "CommonStockId",
                                "CongressMemberId",
                                "TransactionDate",
                                "TransactionType",
                                "AssetName",
                                "OwnerType",
                                "AmountFrom",
                                "AmountTo",
                                "AssetType",
                                "Subholding"
                            ORDER BY "CreationTime", "Id"
                        ) AS row_number
                    FROM "CongressionalTrade"
                )
                DELETE FROM "CongressionalTrade" AS trade
                USING ranked
                WHERE trade."Id" = ranked."Id"
                  AND ranked.row_number > 1;
                """
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CommonStockId",
                table: "CongressionalTrade",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_CongressionalTrade_FilingIdentity",
                table: "CongressionalTrade",
                columns: new[] { "CommonStockId", "CongressMemberId", "TransactionDate", "TransactionType", "AssetName", "OwnerType", "AmountFrom", "AmountTo", "AssetType", "Subholding" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CongressionalTrade_CommonStock_CommonStockId",
                table: "CongressionalTrade",
                column: "CommonStockId",
                principalTable: "CommonStock",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
