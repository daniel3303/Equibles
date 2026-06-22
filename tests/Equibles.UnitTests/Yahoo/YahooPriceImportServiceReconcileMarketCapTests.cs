using System.Reflection;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins <c>ReconcileMarketCap</c>, the pure-logic guard the importer uses to keep a stock's stored
/// market cap consistent with its authoritative share count (#3575/#2503).
///
/// Yahoo's market cap is its own (per-share-class / stale) share count times price. When EDGAR is
/// the authoritative share source, the importer keeps EDGAR's <c>SharesOutStanding</c> but used to
/// store Yahoo's market cap verbatim, so the two figures came from different share bases and
/// disagreed by the share-count ratio. For Idaho Copper (COPR, #3575) a reverse-split lag inflated
/// the Yahoo share count ~20x, so the stored market cap was ~20x too high — and the screener's
/// derived price (market cap ÷ shares) collapsed to a nonsensical figure that appears nowhere in
/// the price history. <c>ReconcileMarketCap</c> rescales Yahoo's market cap onto the EDGAR base so
/// market cap == shares × price again.
/// </summary>
public class YahooPriceImportServiceReconcileMarketCapTests
{
    private static readonly MethodInfo ReconcileMarketCapMethod =
        typeof(YahooPriceImportService).GetMethod(
            "ReconcileMarketCap",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private static double ReconcileMarketCap(
        long? edgarShares,
        long yahooShares,
        double yahooMarketCap
    ) => (double)ReconcileMarketCapMethod.Invoke(null, [edgarShares, yahooShares, yahooMarketCap]);

    [Fact]
    public void ReconcileMarketCap_EdgarSharesDifferFromYahoo_RescalesOntoEdgarBase()
    {
        // COPR (#3575): Yahoo lags a reverse split. Yahoo reports 276,898,105 shares and a market
        // cap of $1,744,458,061 (= the stale share count × the $6.30 close). EDGAR's authoritative
        // post-split count is 14,061,261. The reconciled market cap must equal
        // 14,061,261 × $6.30 ≈ $88.6M (the EDGAR share base × the same price), NOT the stale
        // $1.74B. The pre-fix code stored Yahoo's $1.74B verbatim, which this assertion rejects.
        const long yahooShares = 276_898_105L;
        const double yahooMarketCap = 1_744_458_061d; // ≈ 276,898,105 × $6.30
        const long edgarShares = 14_061_261L;

        var reconciled = ReconcileMarketCap(edgarShares, yahooShares, yahooMarketCap);

        // == edgarShares × the implied $6.30 price == yahooMarketCap × (edgarShares / yahooShares).
        var expected = yahooMarketCap * ((double)edgarShares / yahooShares);
        reconciled.Should().BeApproximately(expected, 1d);
        // Sanity: the corrected figure is ~$88.6M, an order of magnitude below the stale $1.74B.
        reconciled.Should().BeLessThan(100_000_000d);
    }

    [Fact]
    public void ReconcileMarketCap_NoEdgarShareCount_KeepsYahooMarketCap()
    {
        // No EDGAR cover-page count on record (edgarShares == null): Yahoo's market cap and share
        // count are mutually consistent, so the stored value is left exactly as Yahoo reported it.
        const long yahooShares = 50_000_000L;
        const double yahooMarketCap = 315_000_000d;

        var reconciled = ReconcileMarketCap(null, yahooShares, yahooMarketCap);

        reconciled.Should().Be(yahooMarketCap);
    }

    [Fact]
    public void ReconcileMarketCap_YahooShareBaseUnknown_KeepsYahooMarketCap()
    {
        // EDGAR shares are known but Yahoo returned no share count (0 = "unknown" elsewhere in the
        // importer): there is no Yahoo base to rescale from, so degrade to Yahoo's market cap
        // rather than divide by zero / fabricate a figure.
        const double yahooMarketCap = 420_000_000d;

        var reconciled = ReconcileMarketCap(7_500_000L, 0L, yahooMarketCap);

        reconciled.Should().Be(yahooMarketCap);
    }
}
