using System.Globalization;
using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderInstitutionSummaryCultureInvarianceTests
{
    private static readonly MethodInfo RenderInstitutionSummaryMethod =
        typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderInstitutionSummary",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // RenderInstitutionSummary builds the metric cells (Reported AUM :N0,
    // concentration / turnover :F1) with culture-implicit format specifiers
    // that resolve through the thread CurrentCulture. Same bug class as the
    // already-fixed RenderInstitutionPortfolio (GH-2597) and RenderTopHoldersTable
    // (GH-2628) siblings in this class: de-DE swaps the thousand/decimal
    // separators (1,234,567 → 1.234.567, 12.3 → 12,3), forking the LLM-consumed
    // MCP output by host locale. The contract (repo convention; cf. FactMarkdown
    // threading InvariantCulture) is that the same call renders byte-identically
    // regardless of host CurrentCulture.
    [Fact]
    public void RenderInstitutionSummary_UnderNonInvariantCulture_RendersMetricCellsCultureInvariantly()
    {
        var holder = new InstitutionalHolder { Name = "ACME Capital" };
        var targetDate = new DateOnly(2024, 12, 31);
        var summary = new InstitutionPortfolioSummary
        {
            ReportedAum = 1_234_567_890L,
            PositionCount = 1_234,
            Top10ConcentrationPercent = 12.34,
            Top25ConcentrationPercent = 56.78,
            QoQTurnoverPercent = 9.87,
            QuartersReported = 8,
        };
        object[] args = [holder, targetDate, (DateOnly?)new DateOnly(2024, 9, 30), summary];

        var original = CultureInfo.CurrentCulture;
        string invariantOutput;
        string deDeOutput;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariantOutput = (string)RenderInstitutionSummaryMethod.Invoke(null, args);

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            deDeOutput = (string)RenderInstitutionSummaryMethod.Invoke(null, args);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        deDeOutput
            .Should()
            .Be(
                invariantOutput,
                "MCP markdown output is consumed by LLMs trained on en-US conventions; the :N0 / :F1 metric cells without an explicit IFormatProvider follow CurrentCulture (de-DE → 1.234.567.890 / 12,3), forking the response by host locale — same bug class as the RenderInstitutionPortfolio and RenderTopHoldersTable culture-invariance siblings"
            );
    }
}
