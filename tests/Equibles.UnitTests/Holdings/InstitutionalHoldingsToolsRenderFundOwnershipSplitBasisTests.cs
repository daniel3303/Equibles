using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.BusinessLogic.FundOwnership;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderFundOwnershipSplitBasisTests
{
    // A split between the two quarter ends that could not be attributed leaves the earlier
    // quarter on its own pre-split basis: unchanged fund holdings would read as +300.0% after a
    // 4:1 split. The change is withheld when either compared quarter is unresolved.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void RenderFundOwnership_QuarterOnAnotherSplitBasis_ShowsNoChange(
        bool previousUnresolved,
        bool latestUnresolved
    )
    {
        var output = Render(
            Quarter(new DateOnly(2026, 3, 31), 1_000_000, previousUnresolved),
            Quarter(new DateOnly(2026, 6, 30), 4_000_000, latestUnresolved)
        );

        output.Should().NotContain("+300.0%");
        output.Should().Contain("Chg (QoQ) is not shown");
    }

    [Fact]
    public void RenderFundOwnership_BothQuartersOnOneBasis_ShowsTheChange()
    {
        var output = Render(
            Quarter(new DateOnly(2026, 3, 31), 1_000_000, false),
            Quarter(new DateOnly(2026, 6, 30), 1_500_000, false)
        );

        output.Should().Contain("+50.0%");
        output.Should().NotContain("Chg (QoQ) is not shown");
    }

    private static string Render(params FundOwnershipQuarter[] quarters)
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "XYZ",
            Name: "XYZ Corp.",
            Cik: "0000000001"
        );
        var method = typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderFundOwnership",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        return (string)
            method.Invoke(
                null,
                [
                    stock,
                    "XYZ",
                    new FundOwnershipHistory { ListedTicker = null, Quarters = quarters },
                ]
            );
    }

    private static FundOwnershipQuarter Quarter(DateOnly reportDate, long shares, bool unresolved)
    {
        var buckets = FundOwnershipBuckets
            .Ordered.Select(bucket => new FundOwnershipBucketShare
            {
                Bucket = bucket.Value,
                Token = bucket.Token,
                Label = bucket.Label,
                Shares = bucket.Order == 0 ? shares : 0,
                SeriesCount = bucket.Order == 0 ? 3 : 0,
            })
            .ToList();
        return new FundOwnershipQuarter
        {
            ReportDate = reportDate,
            Buckets = buckets,
            Total = new FundOwnershipBucketShare
            {
                Label = "All funds",
                Shares = shares,
                SeriesCount = 3,
            },
            SplitUnresolved = unresolved,
        };
    }
}
