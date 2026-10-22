using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuerSecRegistrantProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "BdcElectionFiledOn",
                table: "EquityIssuer",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BdcHistoryReadOn",
                table: "EquityIssuer",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BdcWithdrawalFiledOn",
                table: "EquityIssuer",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessStateOrCountry",
                table: "EquityIssuer",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PeriodicReportFileNumber",
                table: "EquityIssuer",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StateOfIncorporation",
                table: "EquityIssuer",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BdcElectionFiledOn",
                table: "EquityIssuer");

            migrationBuilder.DropColumn(
                name: "BdcHistoryReadOn",
                table: "EquityIssuer");

            migrationBuilder.DropColumn(
                name: "BdcWithdrawalFiledOn",
                table: "EquityIssuer");

            migrationBuilder.DropColumn(
                name: "BusinessStateOrCountry",
                table: "EquityIssuer");

            migrationBuilder.DropColumn(
                name: "PeriodicReportFileNumber",
                table: "EquityIssuer");

            migrationBuilder.DropColumn(
                name: "StateOfIncorporation",
                table: "EquityIssuer");
        }
    }
}
