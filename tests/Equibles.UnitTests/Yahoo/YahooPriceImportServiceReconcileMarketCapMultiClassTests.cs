using System.Reflection;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Sibling to <see cref="YahooPriceImportServiceReconcileMarketCapTests"/>, which pins the
/// reverse-split direction (#3575, EDGAR shares &lt; Yahoo → scale market cap down). This pins the
/// opposite, equally-promised direction: a multi-class issuer (#2503) where Yahoo reports only one
/// share class, so its share count understates the entity total and EDGAR's authoritative count is
/// <em>larger</em>. The reconciled market cap must scale <em>up</em> onto the larger EDGAR base —
/// never clamp, cap, or collapse to Yahoo's understated figure.
/// </summary>
public class YahooPriceImportServiceReconcileMarketCapMultiClassTests
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
    ) => (double)ReconcileMarketCapMethod.Invoke(null, [edgarShares, yahooShares, yahooMarketCap, null]);

    [Fact]
    public void ReconcileMarketCap_EdgarSharesExceedYahoo_ScalesMarketCapUpOntoEdgarBase()
    {
        // Multi-class issuer (#2503): Yahoo returns only Class A (1.0B shares, $30.0B market cap =
        // 1.0B × $30 price). EDGAR's entity total across all classes is 1.6B. The reconciled market
        // cap must use the full 1.6B base at the same $30 price = $48.0B — strictly GREATER than
        // Yahoo's understated $30.0B, the #2503 half of the contract. A regression that clamped to
        // Yahoo's value, or inverted the ratio, would return ≤ $30.0B and fail here.
        const long yahooShares = 1_000_000_000L;
        const double yahooMarketCap = 30_000_000_000d; // 1.0B × $30
        const long edgarShares = 1_600_000_000L;

        var reconciled = ReconcileMarketCap(edgarShares, yahooShares, yahooMarketCap);

        var expected = yahooMarketCap * ((double)edgarShares / yahooShares); // == 1.6B × $30
        reconciled.Should().BeApproximately(expected, 1d);
        reconciled.Should().BeGreaterThan(yahooMarketCap);
    }
}
