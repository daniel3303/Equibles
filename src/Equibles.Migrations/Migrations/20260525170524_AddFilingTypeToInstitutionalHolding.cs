using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFilingTypeToInstitutionalHolding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~",
                table: "InstitutionalHolding"
            );

            migrationBuilder.AddColumn<int>(
                name: "FilingType",
                table: "InstitutionalHolding",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~",
                    table: "InstitutionalHolding",
                    columns: new[]
                    {
                        "CommonStockId",
                        "InstitutionalHolderId",
                        "ReportDate",
                        "ShareType",
                        "OptionType",
                        "FilingType",
                    },
                    unique: true
                )
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolding_FilingType",
                table: "InstitutionalHolding",
                column: "FilingType"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~",
                table: "InstitutionalHolding"
            );

            migrationBuilder.DropIndex(
                name: "IX_InstitutionalHolding_FilingType",
                table: "InstitutionalHolding"
            );

            migrationBuilder.DropColumn(name: "FilingType", table: "InstitutionalHolding");

            migrationBuilder
                .CreateIndex(
                    name: "IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~",
                    table: "InstitutionalHolding",
                    columns: new[]
                    {
                        "CommonStockId",
                        "InstitutionalHolderId",
                        "ReportDate",
                        "ShareType",
                        "OptionType",
                    },
                    unique: true
                )
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
