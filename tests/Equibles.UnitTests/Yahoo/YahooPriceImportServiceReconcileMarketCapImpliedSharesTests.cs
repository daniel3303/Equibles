using System.Reflection;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Sibling to <see cref="YahooPriceImportServiceReconcileMarketCapTests"/>, pinning the share base
/// the rescale divides by when Yahoo provides <c>impliedSharesOutstanding</c>. Yahoo's
/// <c>summaryDetail.marketCap</c> is the ENTITY-WIDE figure (price × implied shares, all classes),
/// while <c>sharesOutstanding</c> covers only the quoted class — so dividing the market cap by
/// <c>sharesOutstanding</c> treats a full-company cap as a single-class one and inflates every
/// multi-class issuer by the class ratio. GOOGL (quoted Class A ≈ 48% of the entity) stored
/// $9.23T against a true ~$4.44T. The contract: when Yahoo reports an implied count, it — never
/// the single-class count — is the base the cap is rescaled from.
/// </summary>
public class YahooPriceImportServiceReconcileMarketCapImpliedSharesTests
{
    private static readonly MethodInfo ReconcileMarketCapMethod =
        typeof(YahooPriceImportService).GetMethod(
            "ReconcileMarketCap",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private static double ReconcileMarketCap(
        long? edgarShares,
        long yahooShares,
        long yahooImpliedShares,
        double yahooMarketCap
    ) =>
        (double)
            ReconcileMarketCapMethod.Invoke(
                null,
                [edgarShares, yahooShares, yahooImpliedShares, yahooMarketCap, null]
            );

    [Fact]
    public void ReconcileMarketCap_ImpliedSharesProvided_RescalesFromImpliedBaseNotQuotedClass()
    {
        // GOOGL's shape: Yahoo quotes Class A only (5.826B sharesOutstanding) but publishes the
        // entity-wide market cap ($4.44T = $366.46 × the 12.12B implied count across A+B+C).
        // EDGAR's cover-page total is 12.116B. Rescaling from the implied base keeps the cap at
        // ~$4.44T; the pre-fix code divided by the 5.826B quoted-class count and stored $9.23T.
        const long yahooShares = 5_826_000_000L;
        const long yahooImpliedShares = 12_120_000_000L;
        const double yahooMarketCap = 4_440_000_000_000d; // ≈ $366.46 × 12.12B implied
        const long edgarShares = 12_116_000_000L;

        var reconciled = ReconcileMarketCap(
            edgarShares,
            yahooShares,
            yahooImpliedShares,
            yahooMarketCap
        );

        var expected = yahooMarketCap * ((double)edgarShares / yahooImpliedShares); // ≈ $4.439T
        reconciled.Should().BeApproximately(expected, 1_000_000d);
        // The regression this guards against: dividing by the quoted-class count would return
        // ≈ $9.23T — more than double the entity's true market cap.
        reconciled.Should().BeLessThan(5_000_000_000_000d);
    }

    [Fact]
    public void ReconcileMarketCap_ImpliedSharesProvidedButQuotedClassCountMissing_StillRescales()
    {
        // Yahoo occasionally omits sharesOutstanding while still publishing the implied count its
        // market cap is built on. The implied base is usable on its own — the reconcile must not
        // degrade to the verbatim-Yahoo fallback just because the quoted-class count is missing.
        const long yahooImpliedShares = 1_000_000_000L;
        const double yahooMarketCap = 30_000_000_000d; // $30 implied price
        const long edgarShares = 900_000_000L; // buyback landed on EDGAR before Yahoo caught up

        var reconciled = ReconcileMarketCap(edgarShares, 0L, yahooImpliedShares, yahooMarketCap);

        var expected = yahooMarketCap * ((double)edgarShares / yahooImpliedShares); // == $27.0B
        reconciled.Should().BeApproximately(expected, 1d);
    }
}
