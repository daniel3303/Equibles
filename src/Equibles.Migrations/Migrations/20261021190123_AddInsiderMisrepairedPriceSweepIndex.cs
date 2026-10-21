using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddInsiderMisrepairedPriceSweepIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A concurrent build that is interrupted - a restart, a cancelled statement,
            // or the host's migration timeout - leaves an invalid index behind, and every
            // later attempt then fails with "relation already exists". This was not
            // hypothetical: the build stalled behind in-flight transactions while the
            // index was being measured. Drop the invalid leftover first, the same guard
            // the holding repair indexes use.
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_InsiderTransaction_MisrepairedPriceSweep' AND NOT i.indisvalid) THEN "
                    + "EXECUTE 'DROP INDEX ' || quote_ident('IX_InsiderTransaction_MisrepairedPriceSweep'); END IF; "
                    + "END $$;",
                suppressTransaction: true
            );

            migrationBuilder
                .CreateIndex(
                name: "IX_InsiderTransaction_MisrepairedPriceSweep",
                table: "InsiderTransaction",
                columns: new[] { "TransactionDate", "Id" },
                filter: "\"IsPriceValid\" AND NOT \"PriceWasRepaired\"")
                .Annotation("Npgsql:CreatedConcurrently", true)
                .Annotation("Npgsql:IndexInclude", new[] { "FilingDate", "Shares", "PricePerShare", "ReportedPricePerShare" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InsiderTransaction_MisrepairedPriceSweep",
                table: "InsiderTransaction");
        }
    }
}
