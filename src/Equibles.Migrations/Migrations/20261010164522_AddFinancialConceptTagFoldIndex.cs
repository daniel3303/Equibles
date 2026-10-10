using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Btree on the XbrlNames fold of FinancialConcept.Tag, the predicate concept lookups use to
    /// reach every respelling of a tag. Raw SQL with CREATE INDEX CONCURRENTLY
    /// (suppressTransaction) so the build never blocks concept writes during XBRL imports.
    /// </summary>
    public partial class AddFinancialConceptTagFoldIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IF NOT EXISTS would keep a failed CONCURRENTLY build's INVALID index, or a same-named one on another expression.
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_FinancialConcept_TagFold' AND (NOT i.indisvalid "
                    + "OR pg_get_indexdef(c.oid) NOT LIKE '%lower(replace(%')) THEN "
                    + "EXECUTE 'DROP INDEX \"IX_FinancialConcept_TagFold\"'; END IF; END $$;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_FinancialConcept_TagFold\" "
                    + "ON \"FinancialConcept\" (lower(replace(\"Tag\", '_', '')));",
                suppressTransaction: true
            );
            // An index build gathers no expression statistics, and autoanalyze rarely reaches this table.
            migrationBuilder.Sql("ANALYZE \"FinancialConcept\";", suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_FinancialConcept_TagFold\";",
                suppressTransaction: true
            );
        }
    }
}
