using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class StatisticsExtensionsComputeSmaWindowAlignmentTests
{
    // Contract: ComputeSma is "aligned to the input"; positions before the first
    // full window are null, and each subsequent position is the average of the
    // `period` values ENDING at that index (a trailing SMA, the standard for a
    // price chart whose latest point reflects the most recent window). For
    // [1,2,3,4,5] period 2 the trailing windows are (1,2),(2,3),(3,4),(4,5), so
    // index 0 is null and indices 1..4 are 1.5/2.5/3.5/4.5. A forward-looking or
    // misaligned moving average would shift these and silently mislabel every
    // SMA point on the chart by one bar.
    [Fact]
    public void ComputeSma_TrailingWindowOverShortSeries_AlignsAveragesToWindowEnd()
    {
        double[] values = [1, 2, 3, 4, 5];

        var sma = values.ComputeSma(period: 2, digits: 2);

        sma.Should().Equal(new decimal?[] { null, 1.5m, 2.5m, 3.5m, 4.5m });
    }
}
