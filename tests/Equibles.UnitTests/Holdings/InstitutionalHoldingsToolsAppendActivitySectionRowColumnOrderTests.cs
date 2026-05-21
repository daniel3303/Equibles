using System.Reflection;
using System.Text;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsAppendActivitySectionRowColumnOrderTests
{
    private static readonly MethodInfo AppendActivitySectionMethod =
        typeof(InstitutionalHoldingsTools).GetMethod(
            "AppendActivitySection",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // AppendActivitySection (extracted in #1542) emits a row in the order
    // "| {rank} | {Ticker} | {Name} | {Prior} | {New} | {Δ Shares} | {Δ Value} |"
    // — the same column sequence its header line declares. The LLM consumer
    // reads the header to bind columns by position; a refactor that swapped
    // the Ticker/Name order in the row template without updating the header
    // (or vice versa) would silently misattribute every stock's data to the
    // wrong field. Pin both the rank-as-first-cell and the
    // Ticker-before-Name ordering on a single-row input.
    [Fact]
    public void AppendActivitySection_SingleRow_EmitsRankThenTickerThenNameInOrder()
    {
        var result = new StringBuilder();
        var rows = new List<StockPositionChange>
        {
            new()
            {
                Ticker = "TICK",
                Name = "NAME",
                CurrentShares = 0,
                PreviousShares = 0,
                CurrentValue = 0,
                PreviousValue = 0,
            },
        };

        var returned = (bool)AppendActivitySectionMethod.Invoke(null, [result, "Initiated", rows]);

        returned.Should().BeTrue();
        result.ToString().Should().Contain("| 1 | TICK | NAME |");
    }
}
