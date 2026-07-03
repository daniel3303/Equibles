using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class FinancialConceptDescriptionAndBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConceptMetadataCheckedAt",
                table: "FinancialFactsSyncStatus",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Balance",
                table: "FinancialConcept",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "FinancialConcept",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConceptMetadataCheckedAt",
                table: "FinancialFactsSyncStatus");

            migrationBuilder.DropColumn(
                name: "Balance",
                table: "FinancialConcept");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "FinancialConcept");
        }
    }
}
