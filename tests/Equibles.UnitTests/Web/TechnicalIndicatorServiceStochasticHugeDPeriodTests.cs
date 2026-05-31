using Equibles.Yahoo.Repositories;

namespace Equibles.UnitTests.Web;

public class TechnicalIndicatorServiceStochasticHugeDPeriodTests
{
    [Fact(Skip = "GH-2929 — ComputeStochastic warm-up guard overflows int for huge dPeriod")]
    public void ComputeStochastic_DPeriodLargerThanSeries_PercentDIsAllNull()
    {
        // Contract: %D is "null-padded at the start while the lookback window fills"; a
        // dPeriod larger than the series can never fill, so every %D must be null. The
        // GetStochasticOscillator tool only rejects dPeriod < 1, so a near-int.MaxValue
        // dPeriod reaches this method — the warm-up guard (kPeriod-1 + dPeriod-1) must
        // survive it rather than overflowing int and indexing %K out of range.
        var highs = Enumerable.Range(0, 20).Select(i => 10m + i).ToList();
        var lows = Enumerable.Range(0, 20).Select(i => 5m + i).ToList();
        var closes = Enumerable.Range(0, 20).Select(i => 7m + i).ToList();

        var (_, d) = TechnicalIndicatorService.ComputeStochastic(
            highs,
            lows,
            closes,
            kPeriod: 3,
            dPeriod: int.MaxValue
        );

        d.Should().OnlyContain(value => value == null);
    }
}
