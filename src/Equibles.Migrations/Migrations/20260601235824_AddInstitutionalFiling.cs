using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddInstitutionalFiling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstitutionalHolding_InstitutionalHolderId_ReportDate",
                table: "InstitutionalHolding"
            );

            migrationBuilder.CreateTable(
                name: "InstitutionalFiling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessionNumber = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    InstitutionalHolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsAmendment = table.Column<bool>(type: "boolean", nullable: false),
                    PositionCount = table.Column<int>(type: "integer", nullable: false),
                    TotalValue = table.Column<long>(type: "bigint", nullable: false),
                    CreationTime = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstitutionalFiling", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstitutionalFiling_InstitutionalHolder_InstitutionalHolder~",
                        column: x => x.InstitutionalHolderId,
                        principalTable: "InstitutionalHolder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_InstitutionalHolding_InstitutionalHolderId_ReportDate",
                    table: "InstitutionalHolding",
                    columns: new[] { "InstitutionalHolderId", "ReportDate" }
                )
                .Annotation("Npgsql:IndexInclude", new[] { "CommonStockId", "Value", "Shares" });

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalFiling_AccessionNumber",
                table: "InstitutionalFiling",
                column: "AccessionNumber",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalFiling_FilingDate",
                table: "InstitutionalFiling",
                column: "FilingDate"
            );

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalFiling_InstitutionalHolderId",
                table: "InstitutionalFiling",
                column: "InstitutionalHolderId"
            );

            // One-time historical backfill: collapse the existing per-position
            // InstitutionalHolding rows into one InstitutionalFiling row per
            // accession — the same grouping the latest-filings feed used to run
            // inline on every request. Going forward the import path keeps this
            // table current (HoldingsImportService.SyncFilingSummaries). Runs once,
            // when this migration is applied.
            migrationBuilder.Sql(
                """
                INSERT INTO "InstitutionalFiling"
                    ("Id", "AccessionNumber", "InstitutionalHolderId", "FilingDate",
                     "ReportDate", "IsAmendment", "PositionCount", "TotalValue", "CreationTime")
                SELECT
                    gen_random_uuid(),
                    "AccessionNumber",
                    "InstitutionalHolderId",
                    "FilingDate",
                    "ReportDate",
                    "IsAmendment",
                    COUNT(*),
                    COALESCE(SUM("Value"), 0)::bigint,
                    MIN("CreationTime")
                FROM "InstitutionalHolding"
                WHERE "AccessionNumber" IS NOT NULL
                GROUP BY "AccessionNumber", "InstitutionalHolderId", "FilingDate",
                         "ReportDate", "IsAmendment";
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "InstitutionalFiling");

            migrationBuilder.DropIndex(
                name: "IX_InstitutionalHolding_InstitutionalHolderId_ReportDate",
                table: "InstitutionalHolding"
            );

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolding_InstitutionalHolderId_ReportDate",
                table: "InstitutionalHolding",
                columns: new[] { "InstitutionalHolderId", "ReportDate" }
            );
        }
    }
}
