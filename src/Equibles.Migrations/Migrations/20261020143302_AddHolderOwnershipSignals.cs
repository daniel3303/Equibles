using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddHolderOwnershipSignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HolderPositionCostBasis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstitutionalHolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquityIssuerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListedTicker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CostPerShare = table.Column<float[]>(type: "real[]", nullable: false),
                    UnpricedMask = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HolderPositionCostBasis", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HolderPositionCostBasis_InstitutionalHolder_InstitutionalHo~",
                        column: x => x.InstitutionalHolderId,
                        principalTable: "InstitutionalHolder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockQuarterlyHolderOverlap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquityIssuerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListedTicker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    PeerEquityIssuerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeerListedTicker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SharedHolders = table.Column<int>(type: "integer", nullable: false),
                    Similarity = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockQuarterlyHolderOverlap", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockQuarterlyHolderSignals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquityIssuerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListedTicker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Filers = table.Column<int>(type: "integer", nullable: false),
                    Shares = table.Column<long>(type: "bigint", nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    ValuedShares = table.Column<long>(type: "bigint", nullable: false),
                    ConvictionHolders = table.Column<int>(type: "integer", nullable: false),
                    HoldersAboveFivePercent = table.Column<int>(type: "integer", nullable: false),
                    AverageWeight = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    MedianWeight = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    CrowdedShares = table.Column<long>(type: "bigint", nullable: false),
                    CrowdedHolders = table.Column<int>(type: "integer", nullable: false),
                    PrivateFundAdviserShares = table.Column<long>(type: "bigint", nullable: false),
                    CrowdingPercentile = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: true),
                    TenureShortValue = table.Column<long>(type: "bigint", nullable: false),
                    TenureMediumValue = table.Column<long>(type: "bigint", nullable: false),
                    TenureLongValue = table.Column<long>(type: "bigint", nullable: false),
                    TenureCensoredValue = table.Column<long>(type: "bigint", nullable: false),
                    ValueWeightedTenureQuarters = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    Herfindahl = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    TopTenHolderShare = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    PricedShares = table.Column<long>(type: "bigint", nullable: false),
                    PricedValue = table.Column<long>(type: "bigint", nullable: false),
                    EstimatedCostPerShare = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    UnderwaterValueShare = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    ActiveLikeForLikeShares = table.Column<long>(type: "bigint", nullable: true),
                    ActiveLikeForLikePreviousShares = table.Column<long>(type: "bigint", nullable: true),
                    TopScoreNetBuyValue = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                    TopScoreBuyers = table.Column<int>(type: "integer", nullable: true),
                    TopScoreSellers = table.Column<int>(type: "integer", nullable: true),
                    SharesOutstanding = table.Column<long>(type: "bigint", nullable: true),
                    SharesOutstandingAsOf = table.Column<DateOnly>(type: "date", nullable: true),
                    SharesOutstandingBasis = table.Column<int>(type: "integer", nullable: false),
                    InsiderShares = table.Column<long>(type: "bigint", nullable: true),
                    Schedule13DFilers = table.Column<int>(type: "integer", nullable: false),
                    Schedule13GFilers = table.Column<int>(type: "integer", nullable: false),
                    Schedule13GQualifiedInstitutions = table.Column<int>(type: "integer", nullable: false),
                    Schedule13GPassiveInvestors = table.Column<int>(type: "integer", nullable: false),
                    Schedule13GExemptInvestors = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockQuarterlyHolderSignals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HolderPositionCostBasis_InstitutionalHolderId_EquityIssuerI~",
                table: "HolderPositionCostBasis",
                columns: new[] { "InstitutionalHolderId", "EquityIssuerId", "ListedTicker" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_StockQuarterlyHolderOverlap_EquityIssuerId_ListedTicker_Rep~",
                table: "StockQuarterlyHolderOverlap",
                columns: new[] { "EquityIssuerId", "ListedTicker", "ReportDate", "PeerEquityIssuerId", "PeerListedTicker" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_StockQuarterlyHolderOverlap_ReportDate",
                table: "StockQuarterlyHolderOverlap",
                column: "ReportDate");

            migrationBuilder.CreateIndex(
                name: "IX_StockQuarterlyHolderSignals_EquityIssuerId_ListedTicker_Rep~",
                table: "StockQuarterlyHolderSignals",
                columns: new[] { "EquityIssuerId", "ListedTicker", "ReportDate" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_StockQuarterlyHolderSignals_ReportDate",
                table: "StockQuarterlyHolderSignals",
                column: "ReportDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HolderPositionCostBasis");

            migrationBuilder.DropTable(
                name: "StockQuarterlyHolderOverlap");

            migrationBuilder.DropTable(
                name: "StockQuarterlyHolderSignals");
        }
    }
}
