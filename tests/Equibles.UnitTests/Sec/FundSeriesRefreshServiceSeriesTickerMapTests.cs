using Equibles.Integrations.Sec.Models;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

// BuildSeriesTickerMap turns SEC's fund-class ticker directory into the series → class-symbol map.
// Every unambiguous class alias is retained; a symbol claimed by more than one series is excluded.
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

        map.Should().ContainKey("S000014258").WhoseValue.Should().Equal("USD");
    }

    [Fact]
    public void BuildSeriesTickerMap_MultiClassSeriesWithDifferentSymbols_RetainsEveryAlias()
    {
        var map = FundSeriesRefreshService.BuildSeriesTickerMap([
            Row("S000001", "VFIAX", "C1"),
            Row("S000001", "VFFSX", "C2"),
        ]);

        map.Should().ContainKey("S000001").WhoseValue.Should().Equal("VFFSX", "VFIAX");
    }

    [Fact]
    public void BuildSeriesTickerMap_MultipleClassesSameSymbol_StillMaps()
    {
        var map = FundSeriesRefreshService.BuildSeriesTickerMap([
            Row("S000002", "spy", "C1"),
            Row("S000002", "SPY", "C2"),
        ]);

        map.Should().ContainKey("S000002").WhoseValue.Should().Equal("SPY");
    }

    [Fact]
    public void BuildSeriesTickerMap_IndependentSeries_MapIndependently()
    {
        var map = FundSeriesRefreshService.BuildSeriesTickerMap([
            Row("S000014258", "USD"),
            Row("S000001", "VFIAX", "C1"),
            Row("S000001", "VFFSX", "C2"),
        ]);

        map.Should().HaveCount(2);
        map["S000014258"].Should().Equal("USD");
        map["S000001"].Should().Equal("VFFSX", "VFIAX");
    }

    [Fact]
    public void BuildSeriesTickerMap_SymbolClaimedByTwoSeries_IsExcludedFromBoth()
    {
        var map = FundSeriesRefreshService.BuildSeriesTickerMap([
            Row("S000001", "SHARED"),
            Row("S000002", "shared"),
            Row("S000003", "UNIQUE"),
        ]);

        map.Should().ContainSingle();
        map.Should().ContainKey("S000003").WhoseValue.Should().Equal("UNIQUE");
    }
}
