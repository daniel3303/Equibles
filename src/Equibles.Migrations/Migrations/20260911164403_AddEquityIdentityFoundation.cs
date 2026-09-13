using Equibles.Migrations.Infrastructure;
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddEquityIdentityFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            NativeDirectoryEvidenceExpansion20260912.Apply(migrationBuilder);
            migrationBuilder.CreateTable(
                name: "EquityIssuer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdentitySourceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquityIssuer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquityIssuer_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EquitySecurity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquityIssuerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Isin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    SecurityType = table.Column<int>(type: "integer", nullable: false),
                    IdentitySourceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquitySecurity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquitySecurity_EquityIssuer_EquityIssuerId",
                        column: x => x.EquityIssuerId,
                        principalTable: "EquityIssuer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquityListing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquitySecurityId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketIdentifierCode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Ticker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TradingCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    QuoteUnitMultiplier = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    ListedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    DelistedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    IdentitySourceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquityListing", x => x.Id);
                    table.CheckConstraint("CK_EquityListing_Currency", "\"TradingCurrency\" ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("CK_EquityListing_Lifecycle", "(NOT \"Active\" OR \"DelistedOn\" IS NULL) AND (\"ListedOn\" IS NULL OR \"DelistedOn\" IS NULL OR \"ListedOn\" <= \"DelistedOn\")");
                    table.CheckConstraint("CK_EquityListing_Mic", "\"MarketIdentifierCode\" ~ '^[A-Z0-9]{4}$'");
                    table.CheckConstraint("CK_EquityListing_QuoteUnitMultiplier", "\"QuoteUnitMultiplier\" > 0");
                    table.CheckConstraint("CK_EquityListing_Ticker", "length(btrim(\"Ticker\")) > 0 AND \"Ticker\" = btrim(\"Ticker\") AND \"Ticker\" = upper(\"Ticker\")");
                    table.ForeignKey(
                        name: "FK_EquityListing_EquitySecurity_EquitySecurityId",
                        column: x => x.EquitySecurityId,
                        principalTable: "EquitySecurity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquityIssuer_CommonStockId",
                table: "EquityIssuer",
                column: "CommonStockId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquityListing_EquitySecurityId",
                table: "EquityListing",
                column: "EquitySecurityId");

            migrationBuilder.CreateIndex(
                name: "IX_EquityListing_MarketIdentifierCode_Ticker",
                table: "EquityListing",
                columns: new[] { "MarketIdentifierCode", "Ticker" },
                unique: true,
                filter: "\"Active\"");

            migrationBuilder.CreateIndex(
                name: "IX_EquitySecurity_EquityIssuerId",
                table: "EquitySecurity",
                column: "EquityIssuerId");

            migrationBuilder.CreateIndex(
                name: "IX_EquitySecurity_Isin",
                table: "EquitySecurity",
                column: "Isin",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Keep the additive equity identity tables when rolling back application binaries; dropping them would destroy identity data.");
        }
    }
}
