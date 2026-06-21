using System.Reflection;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Sibling to <see cref="YahooPriceImportServiceReconcileMarketCapTests"/>. Pins the guard against a
/// zero authoritative EDGAR share count. The reconcile rescales Yahoo's market cap by
/// edgarShares / yahooShares, so a 0 EDGAR count would multiply the figure by 0 and wipe the stored
/// market cap. The <c>edgarShares is &gt; 0</c> guard exists to stop exactly that: a 0 (or otherwise
/// unusable) EDGAR count must fall back to Yahoo's figure, just as a null count does — the existing
/// null test does not exercise the 0 input, which reaches the fallback through a different leg of
/// the guard and is the dangerous one (a regression to <c>is not null</c> would collapse the cap).
/// </summary>
public class YahooPriceImportServiceReconcileMarketCapZeroEdgarSharesTests
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
    public void ReconcileMarketCap_EdgarShareCountZero_KeepsYahooMarketCap()
    {
        // A zero authoritative EDGAR count (garbage / shell filing) is unusable: rescaling would
        // multiply by edgarShares / yahooShares == 0 and zero the market cap. The contract falls
        // back to Yahoo's figure when there is no usable EDGAR count, so a 0 must be treated like a
        // null count — keep Yahoo's value, never collapse the stored market cap to 0.
        const long yahooShares = 50_000_000L;
        const double yahooMarketCap = 315_000_000d;

        var reconciled = ReconcileMarketCap(0L, yahooShares, yahooMarketCap);

        reconciled.Should().Be(yahooMarketCap);
    }
}
