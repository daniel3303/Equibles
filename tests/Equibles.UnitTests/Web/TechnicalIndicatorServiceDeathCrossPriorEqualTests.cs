using Equibles.Yahoo.Repositories;

namespace Equibles.UnitTests.Web;

public class TechnicalIndicatorServiceDeathCrossPriorEqualTests
{
    // Mirror of DetectMaCross_PriorBarEqual_ThenAbove_ReturnsGoldenCross for the
    // bearish side: a death cross is the short MA being at-or-above the long MA on
    // the prior bar and strictly below on the current bar. Equality on the prior
    // bar must still trigger — the boundary is inclusive (prevShort >= prevLong),
    // symmetric to the golden cross's <=. The golden equality case is pinned; this
    // pins the death-cross equality boundary.
    [Fact]
    public void DetectMaCross_PriorBarEqual_ThenBelow_ReturnsDeathCross()
    {
        List<decimal?> shortMa = [10m, 9m];
        List<decimal?> longMa = [10m, 10m];

        var result = TechnicalIndicatorService.DetectMaCross(shortMa, longMa);

        result.Should().Be(MovingAverageCrossSignal.DeathCross);
    }
}
