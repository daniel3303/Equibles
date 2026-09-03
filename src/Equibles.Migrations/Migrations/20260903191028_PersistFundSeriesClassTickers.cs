using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class PersistFundSeriesClassTickers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "ClassTickers",
                table: "FundSeries",
                type: "text[]",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "FundSeries"
                SET "ClassTickers" = ARRAY[upper(replace("Ticker", '.', '-'))]
                WHERE "Ticker" IS NOT NULL;
                """
            );

            migrationBuilder.CreateIndex(
                name: "IX_FundSeries_ClassTickers_Gin",
                table: "FundSeries",
                column: "ClassTickers")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FundSeries_ClassTickers_Gin",
                table: "FundSeries");

            migrationBuilder.DropColumn(
                name: "ClassTickers",
                table: "FundSeries");
        }
    }
}
