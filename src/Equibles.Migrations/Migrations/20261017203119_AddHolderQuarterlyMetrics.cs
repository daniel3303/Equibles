using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddHolderQuarterlyMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HolderIntelligenceQuarterState",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DirtyAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CalculationVersion = table.Column<int>(type: "integer", nullable: false),
                    FailureCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HolderIntelligenceQuarterState", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HolderQuarterlyMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstitutionalHolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EquityAum = table.Column<long>(type: "bigint", nullable: false),
                    PreviousEquityAum = table.Column<long>(type: "bigint", nullable: true),
                    StockCount = table.Column<int>(type: "integer", nullable: false),
                    BuyValue = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                    SellValue = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                    Turnover = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    TrailingAnnualTurnover = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    NewCount = table.Column<int>(type: "integer", nullable: true),
                    ExitCount = table.Column<int>(type: "integer", nullable: true),
                    IncreasedCount = table.Column<int>(type: "integer", nullable: true),
                    ReducedCount = table.Column<int>(type: "integer", nullable: true),
                    UnpricedTradeCount = table.Column<int>(type: "integer", nullable: true),
                    TopTenWeight = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    Herfindahl = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    EffectivePositions = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    ValueWeightedTenureQuarters = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    CensoredValueShare = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    AggregateActiveShare = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    CallValue = table.Column<long>(type: "bigint", nullable: false),
                    PutValue = table.Column<long>(type: "bigint", nullable: false),
                    PrincipalValue = table.Column<long>(type: "bigint", nullable: false),
                    StyleBand = table.Column<int>(type: "integer", nullable: false),
                    IndexLikeEvidence = table.Column<int>(type: "integer", nullable: false),
                    TurnoverBand = table.Column<int>(type: "integer", nullable: false),
                    HorizonBand = table.Column<int>(type: "integer", nullable: false),
                    BandVersion = table.Column<int>(type: "integer", nullable: false),
                    CalculationVersion = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HolderQuarterlyMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HolderQuarterlyMetrics_InstitutionalHolder_InstitutionalHol~",
                        column: x => x.InstitutionalHolderId,
                        principalTable: "InstitutionalHolder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HolderStockPresence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstitutionalHolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquityIssuerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListedTicker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    HeldMask = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HolderStockPresence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HolderStockPresence_InstitutionalHolder_InstitutionalHolder~",
                        column: x => x.InstitutionalHolderId,
                        principalTable: "InstitutionalHolder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HolderIntelligenceQuarterState_ReportDate",
                table: "HolderIntelligenceQuarterState",
                column: "ReportDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HolderQuarterlyMetrics_InstitutionalHolderId_ReportDate",
                table: "HolderQuarterlyMetrics",
                columns: new[] { "InstitutionalHolderId", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HolderQuarterlyMetrics_ReportDate_StyleBand",
                table: "HolderQuarterlyMetrics",
                columns: new[] { "ReportDate", "StyleBand" });

            migrationBuilder.CreateIndex(
                name: "IX_HolderStockPresence_InstitutionalHolderId_EquityIssuerId_Li~",
                table: "HolderStockPresence",
                columns: new[] { "InstitutionalHolderId", "EquityIssuerId", "ListedTicker" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HolderIntelligenceQuarterState");

            migrationBuilder.DropTable(
                name: "HolderQuarterlyMetrics");

            migrationBuilder.DropTable(
                name: "HolderStockPresence");
        }
    }
}
