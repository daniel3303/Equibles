using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddHolderParentLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FundSeriesHolderLink",
                columns: table => new
                {
                    SeriesId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RegistrantCik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    AdviserCrd = table.Column<int>(type: "integer", nullable: false),
                    InstitutionalHolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Basis = table.Column<int>(type: "integer", nullable: false),
                    NCenReportEndingPeriod = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundSeriesHolderLink", x => x.SeriesId);
                });

            migrationBuilder.CreateTable(
                name: "HolderLinkEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChildCik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ChildHolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParentCik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ParentHolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChildLei = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ParentLei = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AsOf = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HolderLinkEvidence", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HolderRollup",
                columns: table => new
                {
                    Cik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReportingParentCik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    UltimateCik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    UltimateHolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Basis = table.Column<int>(type: "integer", nullable: false),
                    HasConflictingEvidence = table.Column<bool>(type: "boolean", nullable: false),
                    DerivationVersion = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HolderRollup", x => x.Cik);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FundSeriesHolderLink_InstitutionalHolderId",
                table: "FundSeriesHolderLink",
                column: "InstitutionalHolderId");

            migrationBuilder.CreateIndex(
                name: "IX_HolderLinkEvidence_ChildCik_Kind_ReportDate",
                table: "HolderLinkEvidence",
                columns: new[] { "ChildCik", "Kind", "ReportDate" });

            migrationBuilder.CreateIndex(
                name: "IX_HolderLinkEvidence_ParentCik_ReportDate",
                table: "HolderLinkEvidence",
                columns: new[] { "ParentCik", "ReportDate" });

            migrationBuilder.CreateIndex(
                name: "IX_HolderRollup_ReportingParentCik",
                table: "HolderRollup",
                column: "ReportingParentCik");

            migrationBuilder.CreateIndex(
                name: "IX_HolderRollup_UltimateCik",
                table: "HolderRollup",
                column: "UltimateCik");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FundSeriesHolderLink");

            migrationBuilder.DropTable(
                name: "HolderLinkEvidence");

            migrationBuilder.DropTable(
                name: "HolderRollup");
        }
    }
}
