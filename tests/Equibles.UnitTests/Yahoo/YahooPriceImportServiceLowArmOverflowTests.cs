using System.Reflection;
using Equibles.Integrations.Yahoo.Models;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

public class YahooPriceImportServiceLowArmOverflowTests
{
    private static readonly MethodInfo HasOverflowPriceMethod =
        typeof(YahooPriceImportService).GetMethod(
            "HasOverflowPrice",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // Completes the per-arm sweep of HasOverflowPrice's 5-arm OR chain
    // (Open || High || Low || Close || AdjustedClose). Open/High/Close/AdjustedClose are pinned by
    // siblings; Low is the last unpinned interior arm. Dropping the `Math.Abs(p.Low) > Max` clause
    // would compile and pass every sibling, re-introducing a numeric-overflow crash for a corrupted
    // Low value. Adversarial input: only Low exceeds the ceiling → must return true.
    [Fact]
    public void HasOverflowPrice_OnlyLowExceedsCeiling_ReturnsTrue()
    {
        var price = new HistoricalPrice
        {
            Open = 150.25m,
            High = 151.45m,
            Low = 200_000_000_000_000m,
            Close = 150.90m,
            AdjustedClose = 150.50m,
        };

        var result = (bool)HasOverflowPriceMethod.Invoke(null, [price]);

        result.Should().BeTrue();
    }
}
