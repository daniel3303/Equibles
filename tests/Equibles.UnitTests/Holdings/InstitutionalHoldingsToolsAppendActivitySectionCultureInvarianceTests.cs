using System.Globalization;
using System.Reflection;
using System.Text;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsAppendActivitySectionCultureInvarianceTests
{
    private static readonly MethodInfo AppendActivitySectionMethod =
        typeof(InstitutionalHoldingsTools).GetMethod(
            "AppendActivitySection",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // AppendActivitySection renders the Prior / New / Δ Shares cells with bare
    // :N0 specifiers (no IFormatProvider), so they format through the thread
    // CurrentCulture. Same bug class as the already-fixed RenderOverlapTable
    // (#2651) / RenderInstitutionSummary (GH-2637) siblings: under de-DE the
    // share counts gain '.' group separators (1.234.567), forking the LLM-
    // consumed MCP output by host locale. Asserts only the share-count cell so
    // it is independent of the Δ Value cell tracked separately by #2658.
    [Fact(Skip = "GH-2665 — AppendActivitySection emits host-locale digit separators in share-count cells")]
    public void AppendActivitySection_UnderNonInvariantCulture_RendersShareCountsCultureInvariantly()
    {
        var rows = new List<StockPositionChange>
        {
            new()
            {
                Ticker = "AAPL",
                Name = "Apple Inc.",
                PreviousShares = 1_234_567L,
                CurrentShares = 2_000_000L,
                // Equal values → DeltaValue 0 so the Δ Value ($M) cell is "0.0"/"0,0"
                // and cannot contain the share grouping the assertion checks for.
                PreviousValue = 5_000_000_000L,
                CurrentValue = 5_000_000_000L,
            },
        };
        var sb = new StringBuilder();
        object[] args = [sb, "Initiated", rows];

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            AppendActivitySectionMethod.Invoke(null, args);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        sb.ToString()
            .Should()
            .Contain(
                "1,234,567",
                "MCP markdown is consumed by LLMs trained on en-US conventions, so the :N0 share-count cells must use invariant grouping regardless of host culture — under de-DE the bare specifier emits 1.234.567 instead"
            );
    }
}
