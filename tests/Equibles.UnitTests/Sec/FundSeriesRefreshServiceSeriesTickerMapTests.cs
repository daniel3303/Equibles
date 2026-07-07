using Equibles.Integrations.Sec.Models;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

// BuildSeriesTickerMap turns SEC's fund-class ticker directory into the series → symbol map that
// fills FundSeries.Ticker for sweep-discovered trust series (an ETF like ProShares Ultra
// Semiconductors "USD" was unresolvable by ticker because NPORT carries no symbol). The rule under
// test: a series maps ONLY when all its share classes agree on one symbol — a multi-class mutual
// fund has no single ticker, and guessing one class's symbol would misattribute the whole series.
public class FundSeriesRefreshServiceSeriesTickerMapTests
{
    private static FundClassTicker Row(string seriesId, string symbol, string classId = "C1") =>
        new()
        {
            Cik = "1174610",
            SeriesId = seriesId,
            ClassId = classId,
            Symbol = symbol,
        };

    [Fact]
    public void BuildSeriesTickerMap_SingleClassSeries_MapsItsSymbol()
    {
        var map = FundSeriesRefreshService.BuildSeriesTickerMap([Row("S000014258", "USD")]);

        map.Should().ContainKey("S000014258").WhoseValue.Should().Be("USD");
    }

    [Fact]
    public void BuildSeriesTickerMap_MultiClassSeriesWithDifferentSymbols_IsExcluded()
    {
        var map = FundSeriesRefreshService.BuildSeriesTickerMap([
            Row("S000001", "VFIAX", "C1"),
            Row("S000001", "VFFSX", "C2"),
        ]);

        map.Should().BeEmpty("a multi-class fund has no single ticker — guessing misattributes it");
    }

    [Fact]
    public void BuildSeriesTickerMap_MultipleClassesSameSymbol_StillMaps()
    {
        var map = FundSeriesRefreshService.BuildSeriesTickerMap([
            Row("S000002", "spy", "C1"),
            Row("S000002", "SPY", "C2"),
        ]);

        map.Should().ContainKey("S000002");
    }

    [Fact]
    public void BuildSeriesTickerMap_IndependentSeries_MapIndependently()
    {
        var map = FundSeriesRefreshService.BuildSeriesTickerMap([
            Row("S000014258", "USD"),
            Row("S000001", "VFIAX", "C1"),
            Row("S000001", "VFFSX", "C2"),
        ]);

        map.Should().HaveCount(1);
        map["S000014258"].Should().Be("USD");
    }
}
