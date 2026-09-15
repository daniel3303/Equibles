using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Finite correction: one stored row per split event. The same ratio on the same listing
    /// within ten days was captured twice (an announced and an executed date from one provider,
    /// or two providers disagreeing on the day), and every restatement reader applied both.
    /// Keeps the row the capture manager would keep: the highest-precedence source, then the
    /// later date. Completion query: the DELETE's own predicate returns no rows. Retired once
    /// production has none and only writers carrying StockSplitCaptureManager.SameEventWindowDays
    /// are deployed.
    /// </summary>
    public partial class CollapseDuplicateSplitCaptures : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                SET LOCAL lock_timeout = '5s';

                DELETE FROM "StockSplit" duplicate
                WHERE duplicate."EquityListingId" IS NOT NULL
                  AND EXISTS (
                    SELECT 1 FROM "StockSplit" survivor
                    WHERE survivor."Id" <> duplicate."Id"
                      AND survivor."EquityListingId" = duplicate."EquityListingId"
                      AND survivor."Numerator" = duplicate."Numerator"
                      AND survivor."Denominator" = duplicate."Denominator"
                      AND abs(survivor."EffectiveDate" - duplicate."EffectiveDate") <= 10
                      AND (
                        CASE survivor."Source" WHEN 'Manual' THEN 3 WHEN 'SecFiling' THEN 2 WHEN 'External' THEN 1 ELSE 0 END
                          > CASE duplicate."Source" WHEN 'Manual' THEN 3 WHEN 'SecFiling' THEN 2 WHEN 'External' THEN 1 ELSE 0 END
                        OR (
                          survivor."Source" = duplicate."Source"
                          AND (
                            survivor."EffectiveDate" > duplicate."EffectiveDate"
                            OR survivor."EffectiveDate" = duplicate."EffectiveDate" AND survivor."Id" > duplicate."Id"
                          )
                        )
                      )
                  );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The merged observations are not restorable; the writer refuses to recreate them.
        }
    }
}
