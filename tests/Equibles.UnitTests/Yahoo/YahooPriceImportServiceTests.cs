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
}
