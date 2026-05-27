using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderQuarterlyActivityBucketFilterTests
{
    [Fact]
    public void RenderQuarterlyActivity_NormalizedBucketSetToReduced_ShowsOnlyReducedSection()
    {
        // Adversarial sibling to AllEmptyBucketsTests. The
        // `selectedSections` filter (InstitutionalHoldingsTools.cs:1002-1004)
        //   IsNullOrEmpty(normalizedBucket) || section.Label.ToLowerInvariant()
        //       == normalizedBucket
        // gates which of the four bucket headings ("Initiated", "Increased",
        // "Reduced", "Exited") gets rendered. A refactor flipping the OR
        // to AND or dropping the equality check (e.g. "rendering all four
        // is harmless when the LLM filters downstream") would compile,
        // pass the AllEmptyBuckets pin (the fallback still fires when
        // every bucket is empty), and silently emit ALL FOUR sections for
        // every MCP call — quadrupling token usage and obscuring the bucket
        // the user actually asked about. Pin: bucket="reduced" emits
        // exactly the Reduced section heading and none of the others.
        var method = typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderQuarterlyActivity",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var holder = new InstitutionalHolder { Name = "Test Fund" };
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL" };
        var grouped = new Dictionary<StockPositionChangeType, List<StockPositionChange>>
        {
            [StockPositionChangeType.Initiated] =
            [
                new StockPositionChange
                {
                    CommonStockId = stock.Id,
                    Ticker = "INIT",
                    CurrentShares = 100,
                    PreviousShares = 0,
                    CurrentValue = 100_000,
                    PreviousValue = 0,
                },
            ],
            [StockPositionChangeType.Increased] =
            [
                new StockPositionChange
                {
                    CommonStockId = stock.Id,
                    Ticker = "INCR",
                    CurrentShares = 200,
                    PreviousShares = 100,
                    CurrentValue = 200_000,
                    PreviousValue = 100_000,
                },
            ],
            [StockPositionChangeType.Reduced] =
            [
                new StockPositionChange
                {
                    CommonStockId = stock.Id,
                    Ticker = "REDU",
                    CurrentShares = 50,
                    PreviousShares = 200,
                    CurrentValue = 50_000,
                    PreviousValue = 200_000,
                },
            ],
            [StockPositionChangeType.Exited] =
            [
                new StockPositionChange
                {
                    CommonStockId = stock.Id,
                    Ticker = "EXIT",
                    CurrentShares = 0,
                    PreviousShares = 100,
                    CurrentValue = 0,
                    PreviousValue = 100_000,
                },
            ],
        };

        var rendered = (string)
            method!.Invoke(
                null,
                [
                    holder,
                    new DateOnly(2024, 9, 30),
                    new DateOnly(2024, 6, 30),
                    grouped,
                    "reduced",
                    20,
                ]
            );

        rendered.Should().Contain("## Reduced");
        rendered.Should().NotContain("## Initiated");
        rendered.Should().NotContain("## Increased");
        rendered.Should().NotContain("## Exited");
    }
}
