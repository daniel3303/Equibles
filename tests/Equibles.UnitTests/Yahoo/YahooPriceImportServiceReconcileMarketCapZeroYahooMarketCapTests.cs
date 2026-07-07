using System.Reflection;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Sibling to <see cref="YahooPriceImportServiceReconcileMarketCapZeroYahooSharesTests"/>, pinning the
/// THIRD unusable-Yahoo-input leg: a zero Yahoo market cap (Yahoo's <c>summaryDetail.marketCap</c> is
/// missing/unpopulated, common for multi-class issuers — #5238). Without a price fallback, the
/// reconcile has nothing to rescale (<c>yahooMarketCap == 0</c>) and degrades to 0 forever, even
/// though EDGAR's authoritative share count is known and a current price exists. The contract: when
/// EDGAR shares and a current price are both known, compute <c>edgarShares × price</c> directly
/// rather than leaving the stored market cap stale indefinitely.
/// </summary>
public class YahooPriceImportServiceReconcileMarketCapZeroYahooMarketCapTests
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
        double yahooMarketCap,
        decimal? currentPrice
    ) =>
        (double)
            ReconcileMarketCapMethod.Invoke(
                null,
                [edgarShares, yahooShares, 0L, yahooMarketCap, currentPrice]
            );

    [Fact]
    public void ReconcileMarketCap_YahooMarketCapZeroButPriceKnown_ComputesFromEdgarSharesTimesPrice()
    {
        // CHTR-shaped scenario: EDGAR shares are known (122,984,537), Yahoo's own market cap is
        // unusable (0 — Yahoo hasn't reconciled the multi-class figure), but a current price
        // ($146.17) is available from the same import cycle. The result must be the EDGAR share
        // count priced at the current close, not the stale 0.
        const long edgarShares = 122_984_537L;
        const decimal currentPrice = 146.17m;

        var reconciled = ReconcileMarketCap(edgarShares, 0L, 0d, currentPrice);

        reconciled.Should().BeApproximately((double)(edgarShares * currentPrice), 1d);
    }
}
