using Equibles.Yahoo.Repositories;

namespace Equibles.UnitTests.Web;

public class TechnicalIndicatorServiceAtrGapUpTests
{
    // Sibling to TechnicalIndicatorServiceAtrTests
    // .ComputeAtr_GapDownDominatesTrueRange_UsesLowToPreviousCloseTerm.
    // The existing gap-DOWN pin asserts that `|low - prevClose|`
    // dominates Math.Max when the bar opens far below the previous
    // close. This pin completes the symmetric coverage: a bar that
    // opens far ABOVE the previous close, where `|high - prevClose|`
    // (upGap) is the dominant term.
    //
    // Contract (Wilder's True Range, ComputeAtr inline doc):
    //   TR = max(high − low, |high − prev_close|, |low − prev_close|)
    //
    // The Math.Max chain has three arms; the existing test coverage:
    //   • range (high - low) — pinned by various "normal" volatility
    //     tests where range strictly dominates.
    //   • downGap |low - prevClose| — pinned by the gap-DOWN sibling.
    //   • upGap |high - prevClose| — UNPINNED.
    //
    // The risks this pin uniquely catches:
    //
    //   • Dropped upGap from the Math.Max chain — a "simplify" refactor
    //     that removed `Math.Abs(highs[i] - prevClose)` from the chain
    //     (under the false intuition that "downGap is enough" — the
    //     symmetric high-side term is structurally distinct). Every
    //     earnings-surprise gap-up bar (positive guidance, takeover
    //     premium) would silently underreport ATR. The range and
    //     downGap arms keep working — both other pins still pass —
    //     but the upGap arm's contribution is lost.
    //
    //   • upGap → range collapse — `Math.Max(range, downGap)` (drop
    //     the upGap argument) returns 8+10/2 wrong values for any
    //     gap-up bar. The downGap pin still passes (it asserts
    //     downGap dominance, doesn't exercise upGap). The range pin
    //     passes (no gap in that input). The pin must specifically
    //     test upGap > range AND upGap > downGap to catch this.
    //
    //   • Math.Abs drop on upGap (`highs[i] - prevClose` without
    //     Abs) — a "the bar's high is always >= prevClose because
    //     this is a gap-UP" assumption that's false for gap-down
    //     bars. The downGap pin (where high < prevClose) catches
    //     this when downGap is the dominant term, but not when
    //     range dominates and the upGap subtraction is the silent
    //     contributor. Combining the two siblings (this pin's
    //     gap-up + the existing gap-down) covers Abs both sides.
    //
    // Pin construction: bar 0 closes at 100; bar 1 gaps UP to a
    // tight range (H=120, L=110, C=115). Compute Math.Max(range=10,
    // upGap=|120-100|=20, downGap=|110-100|=10) → 20. The 2-period
    // seed averages TR_0=H-L=5 and TR_1=20 → mean = 12.5. Assert
    // atr[1] == 12.5m. A regression that dropped upGap would yield
    // max(10, 10) = 10 → seed = (5 + 10) / 2 = 7.5, NOT 12.5.
    [Fact]
    public void ComputeAtr_GapUpDominatesTrueRange_UsesHighToPreviousCloseTerm()
    {
        // Bar 0: H=100 L=95 C=100 → TR_0 = H − L = 5
        // Bar 1: H=120 L=110 C=115 → prev_close=100 → max(120−110=10,
        //        |120−100|=20, |110−100|=10) = 20
        // 2-period seed at i=1 = mean(5, 20) = 12.5
        var highs = new List<decimal> { 100m, 120m };
        var lows = new List<decimal> { 95m, 110m };
        var closes = new List<decimal> { 100m, 115m };

        var atr = TechnicalIndicatorService.ComputeAtr(highs, lows, closes, 2);

        atr[1].Should().Be(12.5m);
    }
}
