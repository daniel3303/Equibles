using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Index half of the per-listing 13F identity change: widens the InstitutionalHolding
    /// unique index from six columns to seven (adds ListedTicker; NULLS NOT DISTINCT keeps the
    /// all-null primary-class rows unique). Split off from PerListing13FIdentity so the
    /// additive table/column work stays atomic while this one is free to run CONCURRENTLY.
    ///
    /// Raw SQL following the WidenInsiderTransactionCoveringIndex precedent: CONCURRENTLY
    /// (suppressTransaction) so the multi-GB rebuild never holds ACCESS EXCLUSIVE across the
    /// build (the scaffolded pair blocks every 13F reader for minutes on a large install),
    /// create-before-drop so the table keeps unique protection throughout, IF NOT EXISTS so a
    /// hand pre-build ahead of the deploy is a no-op, and an invalid-leftover cleanup for a
    /// failed concurrent build. Every statement converges on re-run, so an interrupted deploy
    /// retries safely.
    ///
    /// One extra step the precedent didn't need: the old six-column and new seven-column index
    /// truncate to the SAME 63-char name, so the replacement builds under a short explicit
    /// name and is renamed onto the EF name once the old index is gone (metadata-only). The
    /// rename fails LOUD if neither the temp index nor an already-renamed valid seven-column
    /// index exists — succeeding silently there would leave a 33M-row table with no unique
    /// protection while the deploy reports green.
    ///
    /// Operational contract:
    /// - Requires deploying ALL hosts together. Once the old index drops, an old-code
    ///   importer's six-column ON CONFLICT has no matching index and fails with 42P10 until
    ///   that host rolls onto the new code; a scoped deploy leaves that window open
    ///   indefinitely. Imports retry on their next cycle, so nothing is lost in the window.
    /// - Effectively one-way: once any row carries a non-null ListedTicker, Down()'s
    ///   six-column rebuild collides on the widened rows and fails. Down exists for the
    ///   pre-rollout window only.
    /// </summary>
    public partial class PerListing13FIndexSwap : Migration
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
                    + "WHERE c.relname = 'IX_InstitutionalHolding_HoldingKeyPerListing' "
                    + "AND c.relkind = 'i' AND c.relnamespace = 'public'::regnamespace "
                    + "AND NOT i.indisvalid) THEN "
                    + "EXECUTE 'DROP INDEX \"IX_InstitutionalHolding_HoldingKeyPerListing\"'; END IF; END $$;",
                suppressTransaction: true
            );
            // With ListedTicker still all-null, NULLS NOT DISTINCT groups the new column
            // exactly like the old six-column key, so the build cannot hit a uniqueness
            // violation.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS \"IX_InstitutionalHolding_HoldingKeyPerListing\" "
                    + "ON \"InstitutionalHolding\" (\"CommonStockId\", \"InstitutionalHolderId\", \"ReportDate\", "
                    + "\"ShareType\", \"OptionType\", \"FilingType\", \"ListedTicker\") NULLS NOT DISTINCT;",
                suppressTransaction: true
            );
            // Only now that the replacement is live and valid: drop the six-column original,
            // freeing the truncated EF name the replacement is then renamed onto. On the rare
            // re-run after a completed swap whose history insert failed, this drops the
            // already-renamed index and the rename below reinstates the fresh temp build — a
            // wasteful but convergent path.
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~\";",
                suppressTransaction: true
            );
            // The rename needs a (brief) lock too — fail fast rather than queue readers.
            migrationBuilder.Sql("SET LOCAL lock_timeout = '3s';");
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_InstitutionalHolding_HoldingKeyPerListing' "
                    + "AND c.relkind = 'i' AND c.relnamespace = 'public'::regnamespace "
                    + "AND i.indisvalid) THEN "
                    + "EXECUTE 'ALTER INDEX \"IX_InstitutionalHolding_HoldingKeyPerListing\" "
                    + "RENAME TO \"IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~\"'; "
                    + "ELSIF NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~' "
                    + "AND c.relkind = 'i' AND c.relnamespace = 'public'::regnamespace "
                    + "AND i.indisvalid AND i.indisunique AND i.indnullsnotdistinct AND i.indnkeyatts = 7) THEN "
                    + "RAISE EXCEPTION 'per-listing holding index missing at rename — InstitutionalHolding has no unique protection'; "
                    + "END IF; END $$;"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Same create-before-drop dance in reverse: rebuild the six-column key under a
            // temporary name, retire the seven-column index, take its name back. Fails (by
            // design) once any non-null ListedTicker rows exist — see the class remarks.
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_InstitutionalHolding_HoldingKeyPrimary' "
                    + "AND c.relkind = 'i' AND c.relnamespace = 'public'::regnamespace "
                    + "AND NOT i.indisvalid) THEN "
                    + "EXECUTE 'DROP INDEX \"IX_InstitutionalHolding_HoldingKeyPrimary\"'; END IF; END $$;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS \"IX_InstitutionalHolding_HoldingKeyPrimary\" "
                    + "ON \"InstitutionalHolding\" (\"CommonStockId\", \"InstitutionalHolderId\", \"ReportDate\", "
                    + "\"ShareType\", \"OptionType\", \"FilingType\") NULLS NOT DISTINCT;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~\";",
                suppressTransaction: true
            );
            migrationBuilder.Sql("SET LOCAL lock_timeout = '3s';");
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_InstitutionalHolding_HoldingKeyPrimary' "
                    + "AND c.relkind = 'i' AND c.relnamespace = 'public'::regnamespace "
                    + "AND i.indisvalid) THEN "
                    + "EXECUTE 'ALTER INDEX \"IX_InstitutionalHolding_HoldingKeyPrimary\" "
                    + "RENAME TO \"IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~\"'; "
                    + "ELSIF NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~' "
                    + "AND c.relkind = 'i' AND c.relnamespace = 'public'::regnamespace "
                    + "AND i.indisvalid AND i.indisunique AND i.indnullsnotdistinct AND i.indnkeyatts = 6) THEN "
                    + "RAISE EXCEPTION 'six-column holding index missing at rename — InstitutionalHolding has no unique protection'; "
                    + "END IF; END $$;"
            );
        }
    }
}
