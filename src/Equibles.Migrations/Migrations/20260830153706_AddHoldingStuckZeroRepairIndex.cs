using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Adds the small partial worklist used by the abandoned holding-value repair. The holdings
    /// table is 72 GB in production, so both retry cleanup and creation run concurrently. The
    /// cleanup makes an interrupted concurrent build retry-safe without taking a table write lock.
    /// </summary>
    public partial class AddHoldingStuckZeroRepairIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InstitutionalHolding_StuckZeroRepair\";",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_InstitutionalHolding_StuckZeroRepair\" "
                    + "ON \"InstitutionalHolding\" (\"Id\") "
                    + "WHERE \"Value\" = 0 AND NOT \"ValuePending\" AND NOT \"ValueUnavailable\" "
                    + "AND \"FiledValue\" IS NOT NULL AND \"FiledValue\" > 0;",
                suppressTransaction: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InstitutionalHolding_StuckZeroRepair\";",
                suppressTransaction: true
            );
        }
    }
}
