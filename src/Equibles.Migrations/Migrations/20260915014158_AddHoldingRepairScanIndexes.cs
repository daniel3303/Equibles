using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Adds the two partial indexes behind the daily holdings repair scans: a small Id worklist
    /// for the implausible-derivation reset, spelled exactly as EF renders that phase's WHERE, and
    /// an (issuer, shares) index over common-share rows above one million shares for the
    /// impossible-position scan. The holdings table is tens of gigabytes in production, so creation
    /// runs concurrently and only an invalid leftover of an interrupted build is dropped first, so
    /// a retry never rebuilds a finished index.
    /// </summary>
    public partial class AddHoldingRepairScanIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_InstitutionalHolding_ImplausibleDerivationRepair' AND NOT i.indisvalid) THEN "
                    + "EXECUTE 'DROP INDEX \"IX_InstitutionalHolding_ImplausibleDerivationRepair\"'; END IF; END $$;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_InstitutionalHolding_ImplausibleDerivationRepair\" "
                    + "ON \"InstitutionalHolding\" (\"Id\") "
                    + "WHERE NOT \"ValuePending\" AND \"ShareType\" = 0 AND \"ValueSource\" <> 1 "
                    + "AND \"Shares\" > 0 AND \"Value\"::numeric > 1000000.0 * \"Shares\"::numeric;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_InstitutionalHolding_ImpossiblePositionRepair' AND NOT i.indisvalid) THEN "
                    + "EXECUTE 'DROP INDEX \"IX_InstitutionalHolding_ImpossiblePositionRepair\"'; END IF; END $$;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_InstitutionalHolding_ImpossiblePositionRepair\" "
                    + "ON \"InstitutionalHolding\" (\"EquityIssuerId\", \"Shares\") "
                    + "WHERE \"ShareType\" = 0 AND NOT \"ValueUnavailable\" AND \"Shares\" > 1000000;",
                suppressTransaction: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InstitutionalHolding_ImplausibleDerivationRepair\";",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InstitutionalHolding_ImpossiblePositionRepair\";",
                suppressTransaction: true
            );
        }
    }
}
