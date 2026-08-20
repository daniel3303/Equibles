using System.Reflection;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Yahoo.HostedService.Services;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// The regression behind the depositary guard, worked in the real figures that exposed it. BeOne
/// Medicines (ONC) lists American Depositary Shares, each representing 13 ordinary shares, and
/// files 10-K/10-Q — so the form-based foreign-private-issuer guard never fires, its cover page
/// counts ordinary shares, and the price feed quotes ADSs.
///
/// <see cref="YahooPriceImportService.ReconcileMarketCap"/> rescales the feed's market cap onto
/// the EDGAR share base. That is right when both are the same unit and catastrophic when they are
/// not: it multiplied a correct ~$42.9B by the deposit ratio and stored $557.0B. The damage hides
/// because the stored pair stays internally consistent — cap ÷ shares still equals the real ADS
/// close of $376.86 — so no downstream sanity check on the pair can see it, and the inflated cap
/// flows into every surface that ranks on market capitalization.
/// </summary>
public class YahooPriceImportServiceAdsMarketCapTests
{
    private static readonly MethodInfo ReconcileMarketCapMethod =
        typeof(YahooPriceImportService).GetMethod(
            "ReconcileMarketCap",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private static double ReconcileMarketCap(long? edgarShares) =>
        (double)
            ReconcileMarketCapMethod.Invoke(
                null,
                [edgarShares, YahooAdsShareBase, 0L, YahooAdsMarketCap, null]
            );

    // ONC as its own SEC cover page reports it: ordinary shares, 13 per ADS.
    private const long EdgarOrdinaryShares = 1_478_124_405L;

    // What production stored for ONC while the defect was live, and the ADS close of the same
    // session. 557,045,957,486.84 / 1,478,124,405 == 376.86 exactly, which is the point: the
    // stored pair looks like a correctly priced company.
    private const double StoredInflatedMarketCap = 557_045_957_486.84d;
    private const double AdsClose = 376.86d;

    // ONC as the price feed reports it, derived from the two above rather than invented: the ADS
    // count is the ordinary count over the deposit ratio, and the feed's market cap is that count
    // at the ADS close. This is the pair the rescale started from.
    private const long YahooAdsShareBase = EdgarOrdinaryShares / 13;
    private const double YahooAdsMarketCap = YahooAdsShareBase * AdsClose;

    private const string AdsTitle =
        "American Depositary Shares, each representing 13 Ordinary Shares, par value $0.0001 per share";

    [Fact]
    public void OrdinaryCoverPageCount_RescalesTheAdsMarketCapByTheDepositRatio()
    {
        // Not an assertion that this is desirable — it is the defect, pinned so the guard below is
        // measured against something real. Feeding the ordinary count in multiplies the correct
        // cap by 13 and produces the $557B that was live in production.
        var inflated = ReconcileMarketCap(EdgarOrdinaryShares);

        // Reproduces the stored figure to within a rounding error of the ADS close, and the shape
        // of the error is exactly the deposit ratio.
        inflated
            .Should()
            .BeApproximately(StoredInflatedMarketCap, StoredInflatedMarketCap * 1e-6);
        (inflated / YahooAdsMarketCap).Should().BeApproximately(13d, 0.001);
    }

    [Fact]
    public void DepositaryTitle_WithholdsTheCoverPageCount_SoTheFeedsMarketCapStands()
    {
        // The guard in SyncKeyStatistics: an issuer whose registered 12(b) title names an American
        // Depositary Share has a cover-page count in a different unit from the quote, so the count
        // is withheld from the rescale. The feed's figure is already on the listed unit and is
        // kept verbatim — repairing the value rather than abstaining from it.
        ListedSecurityClassifier.IsAmericanDepositary(AdsTitle).Should().BeTrue();

        var reconciled = ReconcileMarketCap(null);

        reconciled.Should().Be(YahooAdsMarketCap);
    }

    [Fact]
    public void DepositRatio_SitsFarInsideTheSameUnitTolerance()
    {
        // Why the figures alone cannot catch this, and why the guard has to run BEFORE the ratio
        // check rather than instead of it: 13x is nowhere near the 300x that separates same-unit
        // divergences from different-unit ones, and no real deposit ratio would be — AZN and SNY
        // are 2x. Only the issuer's own registered title distinguishes the two cases.
        ShareBasisPlausibility
            .IsUnitMismatch(EdgarOrdinaryShares, YahooAdsShareBase)
            .Should()
            .BeFalse();
    }
}
