using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFormAdvHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "PrivateFundFocusAsOf",
                table: "InstitutionalHolderProfile",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrivateFundFocusBasis",
                table: "InstitutionalHolderProfile",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VersionedAt",
                table: "FormAdvRosterImport",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LastSeenReportDate",
                table: "FormAdvAdviser",
                type: "date",
                nullable: true);

            // Every stored adviser was last listed by the roster it was imported from.
            migrationBuilder.Sql(
                "UPDATE \"FormAdvAdviser\" SET \"LastSeenReportDate\" = \"ReportDate\";");

            migrationBuilder.CreateTable(
                name: "FormAdvAdviserVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Crd = table.Column<int>(type: "integer", nullable: false),
                    Registration = table.Column<int>(type: "integer", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LastSeenReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SecNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LegalName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Cik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Lei = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    OrganizationForm = table.Column<int>(type: "integer", nullable: true),
                    NumberOfEmployees = table.Column<int>(type: "integer", nullable: true),
                    TotalRegulatoryAum = table.Column<long>(type: "bigint", nullable: true),
                    DiscretionaryAum = table.Column<long>(type: "bigint", nullable: true),
                    NonDiscretionaryAum = table.Column<long>(type: "bigint", nullable: true),
                    RegistrationBasis = table.Column<int>(type: "integer", nullable: false),
                    ExemptionBasis = table.Column<int>(type: "integer", nullable: false),
                    OtherBusinesses = table.Column<int>(type: "integer", nullable: false),
                    RelatedPersons = table.Column<int>(type: "integer", nullable: false),
                    AdvisesPrivateFunds = table.Column<bool>(type: "boolean", nullable: true),
                    PrivateFundCount = table.Column<int>(type: "integer", nullable: true),
                    HedgeFundCount = table.Column<int>(type: "integer", nullable: true),
                    PrivateEquityFundCount = table.Column<int>(type: "integer", nullable: true),
                    VentureCapitalFundCount = table.Column<int>(type: "integer", nullable: true),
                    RealEstateFundCount = table.Column<int>(type: "integer", nullable: true),
                    LiquidityFundCount = table.Column<int>(type: "integer", nullable: true),
                    SecuritizedAssetFundCount = table.Column<int>(type: "integer", nullable: true),
                    OtherPrivateFundCount = table.Column<int>(type: "integer", nullable: true),
                    PrivateFundGrossAssets = table.Column<long>(type: "bigint", nullable: true),
                    LatestFilingDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormAdvAdviserVersion", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormAdvFilingArchiveImport",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Registration = table.Column<int>(type: "integer", nullable: false),
                    ArchiveEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ParserVersion = table.Column<int>(type: "integer", nullable: false),
                    FilingCount = table.Column<int>(type: "integer", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormAdvFilingArchiveImport", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormAdvPrivateFundFiling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Crd = table.Column<int>(type: "integer", nullable: false),
                    Registration = table.Column<int>(type: "integer", nullable: false),
                    FilingId = table.Column<int>(type: "integer", nullable: false),
                    FiledOn = table.Column<DateOnly>(type: "date", nullable: false),
                    FundCount = table.Column<int>(type: "integer", nullable: false),
                    GrossAssets = table.Column<long>(type: "bigint", nullable: false),
                    HedgeFundCount = table.Column<int>(type: "integer", nullable: false),
                    HedgeFundGrossAssets = table.Column<long>(type: "bigint", nullable: false),
                    PrivateEquityFundCount = table.Column<int>(type: "integer", nullable: false),
                    PrivateEquityFundGrossAssets = table.Column<long>(type: "bigint", nullable: false),
                    VentureCapitalFundCount = table.Column<int>(type: "integer", nullable: false),
                    VentureCapitalFundGrossAssets = table.Column<long>(type: "bigint", nullable: false),
                    RealEstateFundCount = table.Column<int>(type: "integer", nullable: false),
                    RealEstateFundGrossAssets = table.Column<long>(type: "bigint", nullable: false),
                    LiquidityFundCount = table.Column<int>(type: "integer", nullable: false),
                    LiquidityFundGrossAssets = table.Column<long>(type: "bigint", nullable: false),
                    SecuritizedAssetFundCount = table.Column<int>(type: "integer", nullable: false),
                    SecuritizedAssetFundGrossAssets = table.Column<long>(type: "bigint", nullable: false),
                    OtherPrivateFundCount = table.Column<int>(type: "integer", nullable: false),
                    OtherPrivateFundGrossAssets = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormAdvPrivateFundFiling", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormAdvClientTypeAum",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FormAdvAdviserVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientType = table.Column<int>(type: "integer", nullable: false),
                    ClientCount = table.Column<int>(type: "integer", nullable: true),
                    FewerThanFiveClients = table.Column<bool>(type: "boolean", nullable: false),
                    RegulatoryAum = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormAdvClientTypeAum", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormAdvClientTypeAum_FormAdvAdviserVersion_FormAdvAdviserVe~",
                        column: x => x.FormAdvAdviserVersionId,
                        principalTable: "FormAdvAdviserVersion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormAdvAdviserVersion_Crd_Registration_ReportDate",
                table: "FormAdvAdviserVersion",
                columns: new[] { "Crd", "Registration", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormAdvAdviserVersion_Registration_ReportDate",
                table: "FormAdvAdviserVersion",
                columns: new[] { "Registration", "ReportDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FormAdvClientTypeAum_FormAdvAdviserVersionId_ClientType",
                table: "FormAdvClientTypeAum",
                columns: new[] { "FormAdvAdviserVersionId", "ClientType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormAdvFilingArchiveImport_Registration_ArchiveEndDate",
                table: "FormAdvFilingArchiveImport",
                columns: new[] { "Registration", "ArchiveEndDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormAdvPrivateFundFiling_Crd_FiledOn",
                table: "FormAdvPrivateFundFiling",
                columns: new[] { "Crd", "FiledOn" });

            migrationBuilder.CreateIndex(
                name: "IX_FormAdvPrivateFundFiling_Registration_FilingId",
                table: "FormAdvPrivateFundFiling",
                columns: new[] { "Registration", "FilingId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormAdvClientTypeAum");

            migrationBuilder.DropTable(
                name: "FormAdvFilingArchiveImport");

            migrationBuilder.DropTable(
                name: "FormAdvPrivateFundFiling");

            migrationBuilder.DropTable(
                name: "FormAdvAdviserVersion");

            migrationBuilder.DropColumn(
                name: "PrivateFundFocusAsOf",
                table: "InstitutionalHolderProfile");

            migrationBuilder.DropColumn(
                name: "PrivateFundFocusBasis",
                table: "InstitutionalHolderProfile");

            migrationBuilder.DropColumn(
                name: "VersionedAt",
                table: "FormAdvRosterImport");

            migrationBuilder.DropColumn(
                name: "LastSeenReportDate",
                table: "FormAdvAdviser");
        }
    }
}
