using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseStatementFigures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReleaseStatementRead",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquityIssuerId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatcherVersion = table.Column<int>(type: "integer", nullable: false),
                    FigureCount = table.Column<int>(type: "integer", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseStatementRead", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseStatementRead_Document_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Document",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReleaseStatementRead_EquityIssuer_EquityIssuerId",
                        column: x => x.EquityIssuerId,
                        principalTable: "EquityIssuer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseStatementFigure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseStatementReadId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquityIssuerId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinancialConceptId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FiledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Value = table.Column<decimal>(type: "numeric", nullable: false),
                    FiscalYear = table.Column<int>(type: "integer", nullable: false),
                    FiscalPeriod = table.Column<int>(type: "integer", nullable: false),
                    SourceLine = table.Column<int>(type: "integer", nullable: false),
                    SourceLabel = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseStatementFigure", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseStatementFigure_EquityIssuer_EquityIssuerId",
                        column: x => x.EquityIssuerId,
                        principalTable: "EquityIssuer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReleaseStatementFigure_FinancialConcept_FinancialConceptId",
                        column: x => x.FinancialConceptId,
                        principalTable: "FinancialConcept",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReleaseStatementFigure_ReleaseStatementRead_ReleaseStatemen~",
                        column: x => x.ReleaseStatementReadId,
                        principalTable: "ReleaseStatementRead",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseStatementFigure_EquityIssuerId_PeriodEnd",
                table: "ReleaseStatementFigure",
                columns: new[] { "EquityIssuerId", "PeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseStatementFigure_FinancialConceptId",
                table: "ReleaseStatementFigure",
                column: "FinancialConceptId");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseStatementFigure_ReleaseStatementReadId_FinancialConc~",
                table: "ReleaseStatementFigure",
                columns: new[] { "ReleaseStatementReadId", "FinancialConceptId", "PeriodStart", "PeriodEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseStatementRead_DocumentId",
                table: "ReleaseStatementRead",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseStatementRead_EquityIssuerId",
                table: "ReleaseStatementRead",
                column: "EquityIssuerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReleaseStatementFigure");

            migrationBuilder.DropTable(
                name: "ReleaseStatementRead");
        }
    }
}
