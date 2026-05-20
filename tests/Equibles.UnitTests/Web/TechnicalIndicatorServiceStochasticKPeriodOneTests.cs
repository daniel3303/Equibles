using Equibles.Yahoo.Repositories;

namespace Equibles.UnitTests.Web;

public class TechnicalIndicatorServiceStochasticKPeriodOneTests
{
    [Fact]
    public void ComputeStochastic_KPeriodOne_PercentKReflectsIntradayClosePosition()
    {
        // Contract: "%K = 100 × (close - lowestLow) / (highestHigh - lowestLow) over a
        // kPeriod lookback". With kPeriod=1 the lookback collapses to the current bar —
        // %K then reports where the close sits within that single bar's H/L range. The
        // close at the midpoint must therefore land at 50; this is a stricter assertion
        // than the existing flat-range guard, which only kicks in when range == 0.
        var highs = new List<decimal> { 10m };
        var lows = new List<decimal> { 5m };
        var closes = new List<decimal> { 7.5m };

        var (k, _) = TechnicalIndicatorService.ComputeStochastic(
            highs,
            lows,
            closes,
            kPeriod: 1,
            dPeriod: 1
        );

        k[0].Should().Be(50m);
    }
}
