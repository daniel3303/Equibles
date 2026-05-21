using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderBuyersSellersTableEmptySellersPlaceholderTests
{
    private static readonly MethodInfo RenderBuyersSellersTableMethod =
        typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderBuyersSellersTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // RenderBuyersSellersTable (extracted in #1554) renders two side-by-side
    // sections — "## Top Buyers" and "## Top Sellers" — each with its own
    // section-specific empty placeholder ("_No buyers this quarter._" vs
    // "_No sellers this quarter._"). A refactor that swapped the empty
    // messages (or threaded the wrong constant through AppendMoverSection's
    // emptyMessage parameter) would emit "_No buyers this quarter._" under
    // "## Top Sellers" — a misleading signal for the LLM consumer that
    // sellers is empty.
    [Fact]
    public void RenderBuyersSellersTable_EmptySellers_EmitsSellersSpecificPlaceholder()
    {
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var topBuyers = new List<(
            string Name,
            long CurrentShares,
            long PreviousShares,
            long DeltaShares,
            long DeltaValue
        )>
        {
            ("Buyer Fund", 100, 50, 50, 1_000_000),
        };
        var topSellers =
            new List<(
                string Name,
                long CurrentShares,
                long PreviousShares,
                long DeltaShares,
                long DeltaValue
            )>();

        var rendered = (string)
            RenderBuyersSellersTableMethod.Invoke(
                null,
                [stock, "AAPL", new DateOnly(2024, 9, 30), (DateOnly?)null, topBuyers, topSellers]
            );

        rendered.Should().Contain("_No sellers this quarter._");
        rendered.Should().NotContain("_No buyers this quarter._");
    }
}
