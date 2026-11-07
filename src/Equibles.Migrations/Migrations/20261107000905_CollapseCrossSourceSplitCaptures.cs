using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Finite correction: one stored row per sizable split event across sources. A chart
    /// provider's stale announced date stood beside the feed's executed date three weeks on
    /// (RUSHA's 3:2 captured on 11 August 2026 and again on 1 September), outside the ten-day
    /// window the earlier collapse used, and every restatement reader applied both. Keeps the
    /// row the capture manager now keeps: the higher-precedence source's date. Same-source
    /// repeats and near-unity ratios (scheduled monthly 101:100 splits) are real events and are
    /// left alone. Completion query: the DELETE's own predicate returns no rows. Retired once
    /// production has none and only writers carrying SplitEvents.CrossSourceSameEventWindowDays
    /// are deployed.
    /// </summary>
    public partial class CollapseCrossSourceSplitCaptures : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                SET LOCAL lock_timeout = '5s';

                DELETE FROM "StockSplit" duplicate
                WHERE duplicate."EquityListingId" IS NOT NULL
                  AND CASE WHEN duplicate."Numerator" > 0 AND duplicate."Denominator" > 0
                        THEN abs(duplicate."Numerator" / duplicate."Denominator" - 1) END >= 0.25
                  AND EXISTS (
                    SELECT 1 FROM "StockSplit" survivor
                    WHERE survivor."Id" <> duplicate."Id"
                      AND survivor."EquityListingId" = duplicate."EquityListingId"
                      AND survivor."Numerator" = duplicate."Numerator"
                      AND survivor."Denominator" = duplicate."Denominator"
                      AND abs(survivor."EffectiveDate" - duplicate."EffectiveDate") <= 45
                      AND CASE survivor."Source" WHEN 'Manual' THEN 3 WHEN 'SecFiling' THEN 2 WHEN 'External' THEN 1 ELSE 0 END
                        > CASE duplicate."Source" WHEN 'Manual' THEN 3 WHEN 'SecFiling' THEN 2 WHEN 'External' THEN 1 ELSE 0 END
                  );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The merged observations are not restorable; the writer refuses to recreate them.
        }
    }
}
