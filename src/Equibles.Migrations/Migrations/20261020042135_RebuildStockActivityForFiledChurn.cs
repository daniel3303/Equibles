using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RebuildStockActivityForFiledChurn : Migration
    {
        // Per-stock activity snapshots counted a filer without a 13F for one of the two quarters
        // as an entry or exit. Stamping every quarter dirty lets AumSnapshotDrainWorker rebuild
        // them one at a time under the corrected rule, after its cooldown. The stamp replaces a
        // pending one or an in-flight lease too, so a rebuild an old container runs during the
        // rollout cannot clear it; imports landing later coalesce on the same flag.
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
