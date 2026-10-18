using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialFactSource : Migration
    {
        private const string FilingXbrlFillIndex = "IX_FinancialFact_FilingXbrlFill";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "FinancialFact",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // FinancialFact is large: build without blocking writers, dropping an invalid leftover of an
            // interrupted build first because IF NOT EXISTS would keep it.
            DropInvalidLeftover(migrationBuilder, FilingXbrlFillIndex);
            migrationBuilder.Sql(
                $"CREATE INDEX CONCURRENTLY IF NOT EXISTS \"{FilingXbrlFillIndex}\" "
                    + "ON \"FinancialFact\" (\"EquityIssuerId\", \"AccessionNumber\") "
                    + "WHERE \"Source\" = 1 AND \"DimensionsKey\" = '';",
                suppressTransaction: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"DROP INDEX CONCURRENTLY IF EXISTS \"{FilingXbrlFillIndex}\";",
                suppressTransaction: true
            );

            migrationBuilder.DropColumn(
                name: "Source",
                table: "FinancialFact");
        }

        private static void DropInvalidLeftover(MigrationBuilder migrationBuilder, string name) =>
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + $"WHERE c.relname = '{name}' AND NOT i.indisvalid) THEN "
                    + $"EXECUTE 'DROP INDEX \"{name}\"'; END IF; END $$;",
                suppressTransaction: true
            );
    }
}
