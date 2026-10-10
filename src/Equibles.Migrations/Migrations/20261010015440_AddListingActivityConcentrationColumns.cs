using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Adds the per-listing figures the exact-listing request reads use (value, filer count and
    /// concentration numerators) to StockQuarterlyListingActivity. Nullable, so rows written by
    /// an older worker read as absent and fall back to the live aggregate until their quarter is
    /// rebuilt. The drain holds this table inside long generation transactions, so the ALTER
    /// takes a short lock_timeout and retries instead of queueing every reader behind an
    /// ACCESS EXCLUSIVE request.
    /// </summary>
    public partial class AddListingActivityConcentrationColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                WithLockRetry(
                    "ALTER TABLE \"StockQuarterlyListingActivity\" "
                        + "ADD COLUMN IF NOT EXISTS \"CurrentValue\" bigint, "
                        + "ADD COLUMN IF NOT EXISTS \"CurrentFilerCount\" integer, "
                        + "ADD COLUMN IF NOT EXISTS \"HolderValueSquaredSum\" double precision, "
                        + "ADD COLUMN IF NOT EXISTS \"TopOneValue\" bigint, "
                        + "ADD COLUMN IF NOT EXISTS \"TopFiveValue\" bigint, "
                        + "ADD COLUMN IF NOT EXISTS \"TopTenValue\" bigint"
                )
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                WithLockRetry(
                    "ALTER TABLE \"StockQuarterlyListingActivity\" "
                        + "DROP COLUMN IF EXISTS \"CurrentValue\", "
                        + "DROP COLUMN IF EXISTS \"CurrentFilerCount\", "
                        + "DROP COLUMN IF EXISTS \"HolderValueSquaredSum\", "
                        + "DROP COLUMN IF EXISTS \"TopOneValue\", "
                        + "DROP COLUMN IF EXISTS \"TopFiveValue\", "
                        + "DROP COLUMN IF EXISTS \"TopTenValue\""
                )
            );
        }

        // Runs one DDL statement under a 3 s lock_timeout, sleeping between attempts for up to
        // twenty minutes; each attempt waits in the lock queue for at most those 3 s, and the
        // timeout is reset so later migrations in the same batch keep the default.
        private static string WithLockRetry(string statement) =>
            "DO $$ DECLARE attempt int := 0; BEGIN LOOP attempt := attempt + 1; "
            + "BEGIN SET LOCAL lock_timeout = '3s'; "
            + $"EXECUTE '{statement.Replace("'", "''")}'; SET LOCAL lock_timeout TO DEFAULT; RETURN; "
            + "EXCEPTION WHEN lock_not_available THEN "
            + "IF attempt >= 200 THEN RAISE; END IF; PERFORM pg_sleep(3); END; "
            + "END LOOP; END $$;";
    }
}
