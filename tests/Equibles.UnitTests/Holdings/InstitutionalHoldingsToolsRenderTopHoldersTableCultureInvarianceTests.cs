using System.Globalization;
using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderTopHoldersTableCultureInvarianceTests
{
    private static readonly MethodInfo RenderTopHoldersTableMethod =
        typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderTopHoldersTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // RenderTopHoldersTable builds the headline + per-row cells with the
    // culture-implicit :N0 (shares), :N1 (value $M), and :F2 (% of total)
    // specifiers, which resolve through the thread CurrentCulture. Same bug
    // class as RenderInstitutionPortfolio (GH-2597, fixed) and the
    // FormatSignedChange / FormatInterval / BuildHoldingKey siblings: de-DE
    // swaps the thousand/decimal separators (1,234,567 → 1.234.567,
    // 1,234.6 → 1.234,6, 12.34% → 12,34%), forking the LLM-consumed output by
    // host locale. The contract (repo convention; cf. FactMarkdown threading
    // InvariantCulture) is that the same call renders byte-identically
    // regardless of host CurrentCulture.
    [Fact]
    public void RenderTopHoldersTable_UnderNonInvariantCulture_RendersCellsCultureInvariantly()
    {
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var holder = new InstitutionalHolder { Name = "ACME Capital" };
        var holdings = new List<InstitutionalHolding>
        {
            new()
            {
                InstitutionalHolder = holder,
                Shares = 1_234_567,
                Value = 1_234_567_890L,
            },
        };
        object[] args =
        [
            stock,
            "AAPL",
            new DateOnly(2024, 12, 31),
            3,
            9_876_543L,
            9_876_543_210L,
            holdings,
        ];

        var original = CultureInfo.CurrentCulture;
        string invariantOutput;
        string deDeOutput;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariantOutput = (string)RenderTopHoldersTableMethod.Invoke(null, args);

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            deDeOutput = (string)RenderTopHoldersTableMethod.Invoke(null, args);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        deDeOutput
            .Should()
            .Be(
                invariantOutput,
                "MCP markdown output is consumed by LLMs trained on en-US conventions; the :N0 / :N1 / :F2 cells without an explicit IFormatProvider follow CurrentCulture (de-DE → 1.234.567 / 1.234,6 / 12,34%), forking the response by host locale — same bug class as the RenderInstitutionPortfolio culture-invariance sibling"
            );
    }
}
