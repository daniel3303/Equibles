using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFormAdvRosterDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AdvisesPrivateFunds",
                table: "FormAdvAdviser",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cik",
                table: "FormAdvAdviser",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CikCount",
                table: "FormAdvAdviser",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExemptionBasis",
                table: "FormAdvAdviser",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HedgeFundCount",
                table: "FormAdvAdviser",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LatestFilingDate",
                table: "FormAdvAdviser",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Lei",
                table: "FormAdvAdviser",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LiquidityFundCount",
                table: "FormAdvAdviser",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationForm",
                table: "FormAdvAdviser",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OtherBusinesses",
                table: "FormAdvAdviser",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OtherPrivateFundCount",
                table: "FormAdvAdviser",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrivateEquityFundCount",
                table: "FormAdvAdviser",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrivateFundCount",
                table: "FormAdvAdviser",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PrivateFundGrossAssets",
                table: "FormAdvAdviser",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RealEstateFundCount",
                table: "FormAdvAdviser",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Registration",
                table: "FormAdvAdviser",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RegistrationBasis",
                table: "FormAdvAdviser",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RelatedPersons",
                table: "FormAdvAdviser",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SecuritizedAssetFundCount",
                table: "FormAdvAdviser",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VentureCapitalFundCount",
                table: "FormAdvAdviser",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FormAdvAdviserClientType",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Crd = table.Column<int>(type: "integer", nullable: false),
                    ClientType = table.Column<int>(type: "integer", nullable: false),
                    ClientCount = table.Column<int>(type: "integer", nullable: true),
                    FewerThanFiveClients = table.Column<bool>(type: "boolean", nullable: false),
                    RegulatoryAum = table.Column<long>(type: "bigint", nullable: true),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormAdvAdviserClientType", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormAdvRosterImport",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Registration = table.Column<int>(type: "integer", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ParserVersion = table.Column<int>(type: "integer", nullable: false),
                    AdviserCount = table.Column<int>(type: "integer", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormAdvRosterImport", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormAdvAdviser_Cik",
                table: "FormAdvAdviser",
                column: "Cik");

            migrationBuilder.CreateIndex(
                name: "IX_FormAdvAdviser_Lei",
                table: "FormAdvAdviser",
                column: "Lei");

            migrationBuilder.CreateIndex(
                name: "IX_FormAdvAdviserClientType_Crd_ClientType",
                table: "FormAdvAdviserClientType",
                columns: new[] { "Crd", "ClientType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormAdvRosterImport_Registration_ReportDate",
                table: "FormAdvRosterImport",
                columns: new[] { "Registration", "ReportDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormAdvAdviserClientType");

            migrationBuilder.DropTable(
                name: "FormAdvRosterImport");

            migrationBuilder.DropIndex(
                name: "IX_FormAdvAdviser_Cik",
                table: "FormAdvAdviser");

            migrationBuilder.DropIndex(
                name: "IX_FormAdvAdviser_Lei",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "AdvisesPrivateFunds",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "Cik",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "CikCount",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "ExemptionBasis",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "HedgeFundCount",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "LatestFilingDate",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "Lei",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "LiquidityFundCount",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "OrganizationForm",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "OtherBusinesses",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "OtherPrivateFundCount",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "PrivateEquityFundCount",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "PrivateFundCount",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "PrivateFundGrossAssets",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "RealEstateFundCount",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "Registration",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "RegistrationBasis",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "RelatedPersons",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "SecuritizedAssetFundCount",
                table: "FormAdvAdviser");

            migrationBuilder.DropColumn(
                name: "VentureCapitalFundCount",
                table: "FormAdvAdviser");
        }
    }
}
