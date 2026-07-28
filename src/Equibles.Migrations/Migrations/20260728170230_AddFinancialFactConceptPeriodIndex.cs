using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Btree on FinancialFact(FinancialConceptId, PeriodEnd) for the queries that ask about a
    /// concept across ALL companies ("has this concept any fact since &lt;date&gt;?"). The existing
    /// company-first index cannot serve those: with CommonStockId unconstrained Postgres restarts
    /// the index search once per distinct stock, measured at 42.6M index searches / 173M buffer
    /// hits / 32s for a single 4,038-concept batch of the concept curation lane.
    ///
    /// Raw SQL with CREATE INDEX CONCURRENTLY (suppressTransaction) so the build takes no write
    /// lock on the live FinancialFact table — the SEC fact ingest writes continuously — and
    /// IF NOT EXISTS keeps it idempotent for deployments that pre-built the index by hand.
    /// Pre-building is the expectation on a large installation: the table is ~156M rows / 84 GB,
    /// and a build running inside the deploy's 900s schema gate would risk aborting the release.
    ///
    /// The new index leads with FinancialConceptId, so it fully covers everything the
    /// single-column FK index served; that one is dropped afterwards rather than before, so no
    /// window exists in which neither index is available.
    /// </summary>
    public partial class AddFinancialFactConceptPeriodIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A failed CONCURRENTLY build leaves an INVALID index behind, which IF NOT EXISTS
            // would then treat as "exists" forever — drop such a leftover first (an invalid
            // index serves no queries, so the plain drop's brief lock is free), then build.
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_FinancialFact_FinancialConceptId_PeriodEnd' AND NOT i.indisvalid) THEN "
                    + "EXECUTE 'DROP INDEX \"IX_FinancialFact_FinancialConceptId_PeriodEnd\"'; END IF; END $$;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_FinancialFact_FinancialConceptId_PeriodEnd\" "
                    + "ON \"FinancialFact\" (\"FinancialConceptId\", \"PeriodEnd\");",
                suppressTransaction: true
            );

            // Redundant once the composite above exists: same leading column, so it serves the
            // FK lookups and cascade deletes the single-column index served.
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_FinancialFact_FinancialConceptId\";",
                suppressTransaction: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_FinancialFact_FinancialConceptId\" "
                    + "ON \"FinancialFact\" (\"FinancialConceptId\");",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_FinancialFact_FinancialConceptId_PeriodEnd\";",
                suppressTransaction: true
            );
        }
    }
}
