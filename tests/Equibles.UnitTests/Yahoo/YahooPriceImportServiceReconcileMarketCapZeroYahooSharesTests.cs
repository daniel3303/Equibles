using System.Reflection;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Sibling to <see cref="YahooPriceImportServiceReconcileMarketCapZeroEdgarSharesTests"/>, pinning the
/// OTHER catastrophic leg of the guard: a zero Yahoo share base. The reconcile divides by
/// yahooShares, so without the <c>yahooShares &gt; 0</c> guard a 0 base would divide to +Infinity and
/// store an Infinity market cap. The contract falls back to Yahoo's figure when there is no usable
/// Yahoo share base to rescale from — so a 0 yahooShares must return Yahoo's market cap unchanged,
/// never Infinity. No existing test exercises the 0 yahooShares input.
/// </summary>
public class YahooPriceImportServiceReconcileMarketCapZeroYahooSharesTests
{
    private static readonly MethodInfo ReconcileMarketCapMethod =
        typeof(YahooPriceImportService).GetMethod(
            "ReconcileMarketCap",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // Pins the legacy feed shape where Yahoo omits impliedSharesOutstanding (0), so
    // sharesOutstanding is the only candidate share base.
    private static double ReconcileMarketCap(
        long? edgarShares,
        long yahooShares,
        double yahooMarketCap
    ) =>
        (double)
            ReconcileMarketCapMethod.Invoke(
                null,
                [edgarShares, yahooShares, 0L, yahooMarketCap, null]
            );

    [Fact]
    public void ReconcileMarketCap_YahooShareCountZero_KeepsYahooMarketCap()
    {
        // A zero Yahoo share base is unusable: rescaling would divide by zero and yield +Infinity.
        // The contract falls back to Yahoo's figure when there is no usable Yahoo share base, so the
        // result must be Yahoo's market cap unchanged — a finite value, never Infinity.
        const long edgarShares = 14_061_261L;
        const double yahooMarketCap = 315_000_000d;

        var reconciled = ReconcileMarketCap(edgarShares, 0L, yahooMarketCap);

        reconciled.Should().Be(yahooMarketCap);
    }
}
