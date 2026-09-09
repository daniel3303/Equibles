using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class CoverNportHolderCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_NportHolding_CusipFiling",
                table: "NportHolding",
                column: "Cusip")
                .Annotation("Npgsql:CreatedConcurrently", true)
                .Annotation("Npgsql:IndexInclude", new[] { "NportFilingId" });

            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_NportHolding_Cusip\";", suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_NportHolding_Cusip",
                table: "NportHolding",
                column: "Cusip")
                .Annotation("Npgsql:CreatedConcurrently", true);

            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_NportHolding_CusipFiling\";", suppressTransaction: true);
        }
    }
}
