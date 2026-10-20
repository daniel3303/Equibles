using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class SeparateUnmatchedStockActivity : Migration
    {
        // The buy and sell movers compared every filer's position, so a filer without a 13F for
        // one quarter read as selling or buying its whole book. The new columns keep those
        // positions apart; stamping every quarter dirty lets AumSnapshotDrainWorker refill them
        // one quarter at a time after its cooldown. Until then they read zero and the movers
        // keep the old figures. The stamp replaces a pending one or an in-flight lease too, so a
        // rebuild an old container runs during the rollout cannot clear it.
        public const string RepairSql = """
            UPDATE "AumQuarterlySnapshot" SET "DirtyAt" = now();
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "UnmatchedCurrentShares",
                table: "StockQuarterlyListingActivity",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "UnmatchedPreviousShares",
                table: "StockQuarterlyListingActivity",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "UnmatchedCurrentShares",
                table: "StockQuarterlyActivity",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "UnmatchedCurrentValue",
                table: "StockQuarterlyActivity",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "UnmatchedPreviousShares",
                table: "StockQuarterlyActivity",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "UnmatchedPreviousValue",
                table: "StockQuarterlyActivity",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql(RepairSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnmatchedCurrentShares",
                table: "StockQuarterlyListingActivity");

            migrationBuilder.DropColumn(
                name: "UnmatchedPreviousShares",
                table: "StockQuarterlyListingActivity");

            migrationBuilder.DropColumn(
                name: "UnmatchedCurrentShares",
                table: "StockQuarterlyActivity");

            migrationBuilder.DropColumn(
                name: "UnmatchedCurrentValue",
                table: "StockQuarterlyActivity");

            migrationBuilder.DropColumn(
                name: "UnmatchedPreviousShares",
                table: "StockQuarterlyActivity");

            migrationBuilder.DropColumn(
                name: "UnmatchedPreviousValue",
                table: "StockQuarterlyActivity");
        }
    }
}
