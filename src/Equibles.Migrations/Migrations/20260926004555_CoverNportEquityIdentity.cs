using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class CoverNportEquityIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_NportHolding_EquityIdentity",
                table: "NportHolding",
                columns: new[] { "Isin", "Cusip", "Lei", "NportFilingId" },
                filter: "\"AssetCategory\" = 'EC' AND \"Isin\" IS NOT NULL")
                .Annotation("Npgsql:CreatedConcurrently", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_NportHolding_EquityIdentity\";",
                suppressTransaction: true);
        }
    }
}
