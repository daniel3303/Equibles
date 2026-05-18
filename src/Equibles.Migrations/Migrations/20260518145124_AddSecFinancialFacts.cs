using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddSecFinancialFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessionNumber",
                table: "Document",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FinancialConcept",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Taxonomy = table.Column<int>(type: "integer", nullable: false),
                    Tag = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Label = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialConcept", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinancialFactsSyncStatus",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastFiledDateSeen = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialFactsSyncStatus", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialFactsSyncStatus_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinancialFact",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinancialConceptId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PeriodType = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Value = table.Column<decimal>(type: "numeric", nullable: false),
                    FiscalYear = table.Column<int>(type: "integer", nullable: false),
                    FiscalPeriod = table.Column<int>(type: "integer", nullable: false),
                    Form = table.Column<string>(type: "text", nullable: false),
                    FiledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AccessionNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Frame = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialFact", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialFact_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FinancialFact_Document_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Document",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinancialFact_FinancialConcept_FinancialConceptId",
                        column: x => x.FinancialConceptId,
                        principalTable: "FinancialConcept",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Document_AccessionNumber",
                table: "Document",
                column: "AccessionNumber");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialConcept_Taxonomy_Tag",
                table: "FinancialConcept",
                columns: new[] { "Taxonomy", "Tag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialFact_CommonStockId_FinancialConceptId_PeriodEnd",
                table: "FinancialFact",
                columns: new[] { "CommonStockId", "FinancialConceptId", "PeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialFact_CommonStockId_FinancialConceptId_Unit_PeriodS~",
                table: "FinancialFact",
                columns: new[] { "CommonStockId", "FinancialConceptId", "Unit", "PeriodStart", "PeriodEnd", "AccessionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialFact_CommonStockId_FiscalYear_FiscalPeriod",
                table: "FinancialFact",
                columns: new[] { "CommonStockId", "FiscalYear", "FiscalPeriod" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialFact_DocumentId",
                table: "FinancialFact",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialFact_FinancialConceptId",
                table: "FinancialFact",
                column: "FinancialConceptId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialFactsSyncStatus_CommonStockId",
                table: "FinancialFactsSyncStatus",
                column: "CommonStockId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancialFact");

            migrationBuilder.DropTable(
                name: "FinancialFactsSyncStatus");

            migrationBuilder.DropTable(
                name: "FinancialConcept");

            migrationBuilder.DropIndex(
                name: "IX_Document_AccessionNumber",
                table: "Document");

            migrationBuilder.DropColumn(
                name: "AccessionNumber",
                table: "Document");
        }
    }
}
