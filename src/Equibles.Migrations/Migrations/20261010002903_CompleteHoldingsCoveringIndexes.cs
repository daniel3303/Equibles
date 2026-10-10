using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Completes three covering indexes so the per-stock quarter book, the holder's common-share
    /// total and the pending-pair scan run index-only: ShareType joins the stock-quarter exposure
    /// and pending-pair includes, OptionType the holder index. An index that still has the old
    /// definition is renamed aside and keeps serving while its replacement builds concurrently under
    /// the canonical name; the old copy is then dropped concurrently.
    /// </summary>
    public partial class CompleteHoldingsCoveringIndexes : Migration
    {
        private const string Exposure = "IX_InstitutionalHolding_StockQuarterExposure";
        private const string Holder = "IX_InstitutionalHolding_InstitutionalHolderId_ReportDate";
        private const string Pairs = "IX_InstitutionalHolding_ValuePending_Pairs";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Replace(
                migrationBuilder,
                Exposure,
                "%\"OptionType\", \"ShareType\")%",
                "ON \"InstitutionalHolding\" (\"EquityIssuerId\", \"ReportDate\") "
                    + "INCLUDE (\"InstitutionalHolderId\", \"Value\", \"Shares\", \"ListedTicker\", "
                    + "\"FilingType\", \"OptionType\", \"ShareType\")"
            );
            Replace(
                migrationBuilder,
                Holder,
                "%\"FilingType\", \"OptionType\")%",
                "ON \"InstitutionalHolding\" (\"InstitutionalHolderId\", \"ReportDate\") "
                    + "INCLUDE (\"EquityIssuerId\", \"Value\", \"Shares\", \"FilingDate\", "
                    + "\"FilingType\", \"OptionType\")"
            );
            Replace(
                migrationBuilder,
                Pairs,
                "%INCLUDE (\"ShareType\")%",
                "ON \"InstitutionalHolding\" (\"EquityIssuerId\", \"ListedTicker\", \"ReportDate\") "
                    + "INCLUDE (\"ShareType\") WHERE \"ValuePending\""
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Replace(
                migrationBuilder,
                Exposure,
                "%\"FilingType\", \"OptionType\")%",
                "ON \"InstitutionalHolding\" (\"EquityIssuerId\", \"ReportDate\") "
                    + "INCLUDE (\"InstitutionalHolderId\", \"Value\", \"Shares\", \"ListedTicker\", "
                    + "\"FilingType\", \"OptionType\")"
            );
            Replace(
                migrationBuilder,
                Holder,
                "%\"FilingDate\", \"FilingType\")%",
                "ON \"InstitutionalHolding\" (\"InstitutionalHolderId\", \"ReportDate\") "
                    + "INCLUDE (\"EquityIssuerId\", \"Value\", \"Shares\", \"FilingDate\", \"FilingType\")"
            );
            Replace(
                migrationBuilder,
                Pairs,
                "%\"ReportDate\") WHERE%",
                "ON \"InstitutionalHolding\" (\"EquityIssuerId\", \"ListedTicker\", \"ReportDate\") "
                    + "WHERE \"ValuePending\""
            );
        }

        // Brings the index under `name` to `definition`. `wanted` is a LIKE pattern that only the
        // wanted definition matches, so a database that already carries it is left untouched and
        // every step can be retried after an interruption.
        private static void Replace(
            MigrationBuilder migrationBuilder,
            string name,
            string wanted,
            string definition
        )
        {
            var old = name + "_old";
            // Renaming takes only SHARE UPDATE EXCLUSIVE, and the renamed index keeps serving
            // queries until its replacement exists. Two copies at once would mean a manual rebuild
            // of the canonical name in between, which this migration refuses to guess about.
            migrationBuilder.Sql(
                "DO $$ DECLARE current text; BEGIN "
                    + $"SELECT pg_get_indexdef(c.oid) INTO current FROM pg_class c WHERE c.relname = '{name}'; "
                    + $"IF current IS NOT NULL AND current NOT LIKE '{wanted}' THEN "
                    + $"IF EXISTS (SELECT 1 FROM pg_class WHERE relname = '{old}') THEN "
                    + $"RAISE EXCEPTION 'both {name} and {old} exist; drop one before migrating'; END IF; "
                    + $"EXECUTE 'ALTER INDEX \"{name}\" RENAME TO \"{old}\"'; END IF; END $$;",
                suppressTransaction: true
            );
            // An interrupted CONCURRENTLY build leaves an invalid index that IF NOT EXISTS would keep.
            // Renaming it aside takes only SHARE UPDATE EXCLUSIVE; a plain DROP would queue every
            // reader and writer of the table behind an ACCESS EXCLUSIVE lock.
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + $"WHERE c.relname = '{name}' AND NOT i.indisvalid) THEN "
                    + $"EXECUTE 'ALTER INDEX \"{name}\" RENAME TO \"{name}_invalid\"'; END IF; END $$;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                $"DROP INDEX CONCURRENTLY IF EXISTS \"{name}_invalid\";",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                $"CREATE INDEX CONCURRENTLY IF NOT EXISTS \"{name}\" {definition};",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                $"DROP INDEX CONCURRENTLY IF EXISTS \"{old}\";",
                suppressTransaction: true
            );
        }
    }
}
