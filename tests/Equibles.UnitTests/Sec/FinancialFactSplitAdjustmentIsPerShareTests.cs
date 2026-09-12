using Equibles.CorporateActions.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// <see cref="FinancialFactSplitAdjustment.IsPerShare"/> decides which stored
/// facts get divided by a stock-split factor before an MCP answer renders
/// them. The facts importer stores every unit SEC companyfacts publishes, and
/// filers report many non-share ratios (USD/bbl, USD/MMBTU, USD/EUR,
/// shares/USD, USD/Shareholder, …) whose values a stock split does not
/// change. A classifier that keys on the mere presence of '/' restates a
/// commodity realization price across a split; only a literal share
/// denominator may qualify, and everything else must stay as filed.
/// </summary>
public class FinancialFactSplitAdjustmentIsPerShareTests
{
    private static FinancialFact Fact(string unit) =>
        new()
        {
            Unit = unit,
            Value = 100m,
            FiledDate = new DateOnly(2020, 1, 2),
        };

    [Theory]
    [InlineData("USD/shares")]
    [InlineData("CAD/shares")]
    [InlineData("EUR/shares")]
    [InlineData("USD/Shares")]
    [InlineData("USD/share")]
    [InlineData("USD/Share")]
    public void IsPerShare_ShareDenominatedRatio_IsTrue(string unit)
    {
        FinancialFactSplitAdjustment.IsPerShare(Fact(unit)).Should().BeTrue();
    }

    [Theory]
    [InlineData("USD/bbl")]
    [InlineData("USD/MMBTU")]
    [InlineData("USD/MWh")]
    [InlineData("USD/oz")]
    [InlineData("USD/EUR")]
    [InlineData("USD/item")]
    [InlineData("USD/loan")]
    [InlineData("bbl/D")]
    [InlineData("USD/Shareholder")]
    [InlineData("USD/derivativeShare")]
    [InlineData("USD/shares_unit")]
    [InlineData("MXN/pershare")]
    [InlineData("USD/oneTen-thousandthShare")]
    public void IsPerShare_NonShareRatio_IsFalse(string unit)
    {
        FinancialFactSplitAdjustment.IsPerShare(Fact(unit)).Should().BeFalse();
    }

    [Fact]
    public void IsPerShare_InvertedShareNumerator_IsFalse()
    {
        // shares/USD is share-count per dollar: a split multiplies it, so the
        // per-share divide is wrong in both classification and direction.
        FinancialFactSplitAdjustment.IsPerShare(Fact("shares/USD")).Should().BeFalse();
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("shares")]
    [InlineData("pure")]
    [InlineData("/shares")]
    [InlineData("USD/shares/shares")]
    [InlineData(null)]
    public void IsPerShare_NonRatioOrMalformedUnit_IsFalse(string unit)
    {
        FinancialFactSplitAdjustment.IsPerShare(Fact(unit)).Should().BeFalse();
    }

    [Fact]
    public void Restate_NonShareRatioAcrossSplit_StaysAsFiled()
    {
        var fact = Fact("USD/bbl");
        var listingId = Guid.NewGuid();
        var splits = new List<StockSplit>
        {
            new()
            {
                EquityListingId = listingId,
                EffectiveDate = new DateOnly(2022, 7, 18),
                Numerator = 10m,
                Denominator = 1m,
            },
        };

        var value = FinancialFactSplitAdjustment.Restate(
            fact,
            splits,
            listingId,
            out var adjusted,
            out var unresolved
        );

        unresolved.Should().BeFalse();
        value.Should().Be(100m);
        adjusted.Should().BeFalse();
    }

    [Fact]
    public void Restate_ShareDenominatedRatioAcrossSplit_IsDivided()
    {
        var fact = Fact("USD/shares");
        var listingId = Guid.NewGuid();
        var splits = new List<StockSplit>
        {
            new()
            {
                EquityListingId = listingId,
                EffectiveDate = new DateOnly(2022, 7, 18),
                Numerator = 10m,
                Denominator = 1m,
            },
        };

        var value = FinancialFactSplitAdjustment.Restate(
            fact,
            splits,
            listingId,
            out var adjusted,
            out var unresolved
        );

        unresolved.Should().BeFalse();
        value.Should().Be(10m);
        adjusted.Should().BeTrue();
    }

    [Theory]
    [InlineData("unattributed")]
    [InlineData("invalid")]
    [InlineData("duplicate")]
    [InlineData("other-listing")]
    [InlineData("renamed")]
    public void Restate_NativeAttribution_PreservesUnresolvedValuesAndIsolatesOtherListings(
        string scope
    )
    {
        var fact = Fact("USD/shares");
        var listingId = Guid.NewGuid();
        var split = new StockSplit
        {
            EquityListingId =
                scope == "unattributed" ? null
                : scope == "other-listing" ? Guid.NewGuid()
                : listingId,
            PriceSeriesTicker = "OLD-SOURCE-SYMBOL",
            EffectiveDate = new DateOnly(2022, 7, 18),
            Numerator = scope == "invalid" ? 0 : 10m,
            Denominator = 1m,
        };
        var splits = new List<StockSplit> { split };
        if (scope == "duplicate")
            splits.Add(split);
        var value = FinancialFactSplitAdjustment.Restate(
            fact,
            splits,
            listingId,
            out var adjusted,
            out var unresolved
        );
        unresolved.Should().Be(scope is "unattributed" or "invalid" or "duplicate");
        adjusted.Should().Be(scope == "renamed");
        value.Should().Be(scope == "renamed" ? 10m : 100m);
    }
}
