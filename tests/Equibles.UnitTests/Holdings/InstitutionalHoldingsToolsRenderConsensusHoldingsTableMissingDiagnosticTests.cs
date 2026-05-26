using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderConsensusHoldingsTableMissingDiagnosticTests
{
    private static readonly MethodInfo RenderConsensusHoldingsTableMethod =
        typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderConsensusHoldingsTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // RenderConsensusHoldingsTable renders the GetConsensusHoldings output for
    // the LLM consumer. When the caller supplies institution names that don't
    // resolve to a holder (missing is non-empty), they must be surfaced even
    // when the minFunds threshold filters out every consensus row — otherwise
    // the operator can't tell whether the empty result is due to the threshold
    // or to a typo in their fund list. A refactor that placed the missing-line
    // render inside the `rowsWithConsensus.Count > 0` branch (i.e. after the
    // early return on empty rows) would silently drop that diagnostic.
    [Fact]
    public void RenderConsensusHoldingsTable_MissingNamesWithEmptyRows_PreservesMissingDiagnostic()
    {
        var holders = new List<InstitutionalHolder>
        {
            new() { Name = "Berkshire Hathaway", Cik = "0001067983" },
        };
        var missing = new List<string> { "FAKE FUND", "ANOTHER FAKE" };
        var selected = new DateOnly(2024, 9, 30);
        var rowsWithConsensus = new List<(FundOverlapRow Row, int HeldBy)>();

        var rendered = (string)
            RenderConsensusHoldingsTableMethod.Invoke(
                null,
                [holders, missing, selected, rowsWithConsensus]
            );

        rendered.Should().Contain("FAKE FUND, ANOTHER FAKE");
        rendered.Should().Contain("_No stocks meet the minFunds threshold._");
    }
}
