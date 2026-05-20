using System.Reflection;
using Equibles.Integrations.Yahoo.Models;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

public class YahooPriceImportServiceAdjustedCloseOverflowTests
{
    private static readonly MethodInfo HasOverflowPriceMethod =
        typeof(YahooPriceImportService).GetMethod(
            "HasOverflowPrice",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // Sibling to HasOverflowPrice_AbsValueExceedsNumeric18_4Ceiling_ReturnsTrue
    // (covers `Open`) and …_AllFieldsWithinNumericCeiling_ReturnsFalse. The
    // overflow check is a 5-arm OR chain (Open || High || Low || Close ||
    // AdjustedClose); only the first and the all-normal cases are pinned.
    // AdjustedClose is the last arm — the easiest to silently drop during a
    // refactor — and the most volatile field in practice (Yahoo's split /
    // dividend back-adjustments can fabricate extreme values long after the
    // raw OHLC has stabilised). A regression that dropped the AdjustedClose
    // clause would silently re-enable the numeric(18,4) overflow path for
    // every adjusted-close outlier in the entire daily batch.
    [Fact]
    public void HasOverflowPrice_OnlyAdjustedCloseExceedsCeiling_ReturnsTrue()
    {
        var price = new HistoricalPrice
        {
            Open = 150.25m,
            High = 152.80m,
            Low = 149.10m,
            Close = 151.45m,
            AdjustedClose = 200_000_000_000_000m,
        };

        var result = (bool)HasOverflowPriceMethod.Invoke(null, [price]);

        result.Should().BeTrue();
    }
}
