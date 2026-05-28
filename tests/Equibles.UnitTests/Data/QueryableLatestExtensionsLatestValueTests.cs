using Equibles.Data.Extensions;

namespace Equibles.UnitTests.Data;

public class QueryableLatestExtensionsLatestValueTests
{
    // Adversarial Lane A. The docstring on LatestValue commits to a strict
    // contract: "returns the single highest value as an IQueryable<TKey>
    // (ORDER BY ... DESC LIMIT 1)". Every repository "GetLatest…" path
    // depends on this — CboeVixDailyRepository, CboePutCallRatioRepository,
    // CftcPositionReportRepository (×2), FredObservationRepository,
    // DailyStockPriceRepository all chain into LatestValue and feed a single
    // FirstOrDefault/Single. A regression that drops `OrderByDescending`,
    // drops `.Take(1)`, or inverts the ordering would silently surface a
    // stale or non-extremum date as "the latest", forking every scraper's
    // catch-up logic and every MCP "as of" answer.
    //
    // The adversarial input: a sequence with the maximum repeated AND
    // out-of-order, mixed with smaller values. The single assertion
    // (returns exactly one row equal to the max) catches simultaneously:
    //   • OrderBy / OrderByDescending swap (would return the minimum, 1).
    //   • Dropped .Take(1) (would return ≥2 rows because the max repeats).
    //   • Distinct-only "fix" without ordering (Distinct returns rows in
    //     provider order; for LINQ-to-Objects that's insertion order, so
    //     the first row would be 5 — wrong).
    //   • Selector swapped to a constant projection (would return the
    //     constant, not the max).
    [Fact]
    public void LatestValue_DuplicateMaxValuesUnordered_ReturnsSingleHighestValue()
    {
        var source = new[] { 5, 3, 8, 3, 8, 1 }.AsQueryable();

        var result = source.LatestValue(x => x).ToList();

        result.Should().ContainSingle().Which.Should().Be(8);
    }
}
