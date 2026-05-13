using System.Reflection;
using Equibles.Integrations.Yahoo.Models;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Tests for <see cref="YahooPriceImportService"/>. The public entry point pulls
/// quotes from Yahoo Finance and writes them to the database; we exercise the
/// pure-logic private static guard <c>HasOverflowPrice</c> via reflection.
/// </summary>
public class YahooPriceImportServiceTests {
    private static readonly MethodInfo HasOverflowPriceMethod = typeof(YahooPriceImportService)
        .GetMethod("HasOverflowPrice", BindingFlags.NonPublic | BindingFlags.Static);

    [Fact]
    public void HasOverflowPrice_AbsValueExceedsNumeric18_4Ceiling_ReturnsTrue() {
        // The Yahoo importer batches into a numeric(18,4) column; values that exceed
        // 99_999_999_999_999.9999 will throw on SaveChanges and abort the entire
        // batch. HasOverflowPrice is the filter that keeps a single garbage quote —
        // Yahoo occasionally returns absurd values for thinly traded tickers — from
        // poisoning the day's import for every other stock. Pin the abs() comparison
        // so a refactor that drops the absolute-value check (allowing large negative
        // values through) doesn't silently re-enable the overflow path.
        var price = new HistoricalPrice {
            Open = -200_000_000_000_000m, // |value| > MaxPriceValue, negative side
            High = 1m,
            Low = 1m,
            Close = 1m,
            AdjustedClose = 1m
        };

        var result = (bool)HasOverflowPriceMethod.Invoke(null, [price]);

        result.Should().BeTrue();
    }

    [Fact]
    public void HasOverflowPrice_AllFieldsWithinNumericCeiling_ReturnsFalse() {
        // Sibling to the overflow-true pin above. The risk this catches is
        // asymmetric and unreachable from the existing sibling: a regression
        // that hard-codes `HasOverflowPrice => true` (defensive default during
        // a refactor, or copy-paste from an "always-filter" path) passes the
        // overflow-case test and only shows up here. Without this pin, an
        // "always-true" regression silently filters EVERY price quote out of
        // every batch — all Yahoo imports would import zero rows per cycle,
        // and the failure mode is invisible because HasOverflowPrice doesn't
        // log (the filter is applied silently inside a LINQ Where on a per-
        // row basis). Operators discover days later when historical-price
        // dashboards show flatlines on every active ticker.
        //
        // The pair (overflow → true, normal → false) distinguishes a working
        // OR-chain from BOTH negation (`Math.Abs(p.Open) <= MaxPriceValue ||
        // ...` → normal-case returns true, caught here) AND constant-true
        // collapse (also caught here). Use realistic stock-price magnitudes
        // (sub-$10K) far inside the numeric(18,4) ceiling so a refactor that
        // tightened the threshold by a factor of 10⁹ or 10¹⁰ (still wildly
        // above realistic prices) wouldn't accidentally trigger.
        var price = new HistoricalPrice {
            Open = 150.25m,
            High = 152.80m,
            Low = 149.10m,
            Close = 151.45m,
            AdjustedClose = 151.45m
        };

        var result = (bool)HasOverflowPriceMethod.Invoke(null, [price]);

        result.Should().BeFalse();
    }
}
