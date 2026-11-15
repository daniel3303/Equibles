using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RebuildStockActivityForCommonShareTotals : Migration
    {
        // Per-stock activity snapshots summed put, call and principal rows into a stock's shares
        // and value. Stamping every quarter dirty lets AumSnapshotDrainWorker rebuild them one at
        // a time under the common-share rule, after its cooldown: recent quarters first, older
        // ones a day later, so a trend mixes both bases until the drain finishes. The stamp
        // replaces a pending one or an in-flight lease too, so a rebuild an old container runs
        // during the rollout cannot clear it; imports landing later coalesce on the same flag.
        public const string RepairSql = """
            UPDATE "AumQuarterlySnapshot" SET "DirtyAt" = now();
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RepairSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
