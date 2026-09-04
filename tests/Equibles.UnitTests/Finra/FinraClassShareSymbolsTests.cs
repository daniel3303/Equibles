using Equibles.CommonStocks.Data.Models;
using Equibles.Finra.HostedService.Services;
using Equibles.Integrations.Finra.Models;

namespace Equibles.UnitTests.Finra;

// FINRA spells one class share three ways across its feeds (daily files "BRK/B", weekly OTC
// "BRK.B", consolidated short interest "BRKB") while stored tickers use the dash convention.
// These pins guard the deterministic spelling bridge (#4369): resolution priority, the
// ambiguity rules on the compressed index (absent beats wrong), case preservation, and the
// outbound request spellings the symbol-filtered short-interest fetch depends on.
public class FinraClassShareSymbolsTests
{
    [Fact]
    public void DotToDash_MapsDottedClassSpellingCasePreserving()
    {
        FinraClassShareSymbols.DotToDash("BRK.B").Should().Be("BRK-B");
        FinraClassShareSymbols.DotToDash("BF.B").Should().Be("BF-B");
        FinraClassShareSymbols.DotToDash("TpC.A").Should().Be("TpC-A");
        FinraClassShareSymbols.DotToDash("AAPL").Should().Be("AAPL");
        FinraClassShareSymbols.DotToDash(null).Should().BeNull();
    }

    [Fact]
    public void BuildCompressedIndex_IndexesDashTickers_AndDropsAmbiguity()
    {
        var brkB = Guid.NewGuid();
        var realBfb = Guid.NewGuid();
        var bfB = Guid.NewGuid();
        var colliding1 = Guid.NewGuid();
        var colliding2 = Guid.NewGuid();
        var tickerMap = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            ["BRK-B"] = brkB,
            // A real stored ticker that IS another ticker's compressed spelling: the class
            // share must not shadow it, so no index entry is created.
            ["BFB"] = realBfb,
            ["BF-B"] = bfB,
            // Two dash tickers compressing onto one spelling: both are dropped.
            ["XY-Z"] = colliding1,
            ["X-YZ"] = colliding2,
        };

        var index = FinraClassShareSymbols.BuildCompressedIndex(tickerMap, StringComparer.Ordinal);

        index.Should().ContainKey("BRKB").WhoseValue.Should().Be(brkB);
        index.Should().NotContainKey("BFB");
        index.Should().NotContainKey("XYZ");
    }

    [Fact]
    public void TryResolve_PrefersExactTicker_ThenDotted_ThenCompressed()
    {
        var exact = Guid.NewGuid();
        var dashed = Guid.NewGuid();
        var tickerMap = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            ["BRKB"] = exact,
            ["BRK-B"] = dashed,
        };
        var index = FinraClassShareSymbols.BuildCompressedIndex(tickerMap, StringComparer.Ordinal);

        // "BRKB" is a real stored ticker, so the compressed class spelling never shadows it.
        FinraClassShareSymbols.TryResolve(tickerMap, index, "BRKB", out var id).Should().BeTrue();
        id.Should().Be(exact);

        FinraClassShareSymbols
            .TryResolve(tickerMap, index, "BRK.B", out var dotted)
            .Should()
            .BeTrue();
        dotted.Should().Be(dashed);

        FinraClassShareSymbols.TryResolve(tickerMap, index, "MISSING", out _).Should().BeFalse();
        FinraClassShareSymbols.TryResolve(tickerMap, index, null, out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolve_CompressedSpelling_ResolvesWhenUnambiguous()
    {
        var bfB = Guid.NewGuid();
        var tickerMap = new Dictionary<string, Guid>(StringComparer.Ordinal) { ["BF-B"] = bfB };
        var index = FinraClassShareSymbols.BuildCompressedIndex(tickerMap, StringComparer.Ordinal);

        FinraClassShareSymbols.TryResolve(tickerMap, index, "BFB", out var id).Should().BeTrue();
        id.Should().Be(bfB);
    }

    [Fact]
    public void RequestSpellings_ClassShareYieldsAllThreeForms()
    {
        FinraClassShareSymbols.RequestSpellings("BRK-B").Should().Equal("BRK-B", "BRKB", "BRK.B");
        FinraClassShareSymbols.RequestSpellings("AAPL").Should().Equal("AAPL");
    }

    // End-to-end through the weekly merger: a dotted class-share row lands on the dash-stored
    // stock instead of dropping silently.
    [Fact]
    public void Merge_DottedClassShareSymbol_ResolvesToDashTicker()
    {
        var stockId = Guid.NewGuid();
        var security = new ListedSecurityKey(stockId, "BRK-B");
        var tickerMap = new Dictionary<string, ListedSecurityKey>(StringComparer.Ordinal)
        {
            ["BRK-B"] = security,
        };
        var index = FinraClassShareSymbols.BuildCompressedIndex(tickerMap, StringComparer.Ordinal);
        var records = new List<OffExchangeWeeklyRecord>
        {
            new()
            {
                Symbol = "BRK.B",
                SummaryTypeCode = "ATS_W_SMBL",
                TotalWeeklyShareQuantity = 5_000,
                TotalWeeklyTradeCount = 50,
            },
        };

        var result = OffExchangeVolumeMerger.Merge(
            records,
            tickerMap,
            index,
            new DateOnly(2024, 3, 4)
        );

        result.Should().ContainSingle();
        result[security].AtsVolume.Should().Be(5_000);
    }
}
