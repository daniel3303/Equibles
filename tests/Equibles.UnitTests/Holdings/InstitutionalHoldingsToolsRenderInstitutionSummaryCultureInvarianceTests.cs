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

    // RenderInstitutionSummary builds the metric cells (tracked 13F value :N0,
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
        object[] args =
        [
            holder,
            targetDate,
            (DateOnly?)new DateOnly(2024, 9, 30),
            summary,
            null,
            null,
            null,
        ];

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

    [Fact]
    public void RenderInstitutionSummary_WithAProfile_RendersProfileCellsCultureInvariantly()
    {
        var holder = new InstitutionalHolder { Name = "ACME Capital" };
        var profile = new InstitutionalHolderProfile
        {
            EntityType = HolderEntityType.RegisteredAdviser,
            EntityTypeSource = HolderEvidenceSource.FormAdvRoster,
            EntityTypeSourceKey = "1234",
            EntityTypeAsOf = new DateOnly(2026, 10, 1),
            AdviserCrd = 1234,
            AdviserAsOf = new DateOnly(2026, 10, 1),
            RegulatoryAum = 5_000_000_000,
            ClientBase = HolderClientBase.PooledInvestmentVehicles,
            ClientBaseAumShare = 0.625m,
            PooledVehicleAumShare = 0.4375m,
            AdvisesPrivateFunds = true,
            PrivateFundCount = 1_234,
            PrivateFundFocus = PrivateFundFocus.Mixed,
            PrivateFundGrossAssets = 9_876_543_210,
            AdvisedSeriesCount = 1_200,
            IndexSeriesCount = 1_100,
            EtfSeriesCount = 1_050,
            AdvisedNetAssets = 1_234_567_890.12m,
            AdvisedSeriesAsOf = new DateOnly(2026, 6, 30),
        };
        object[] args =
        [
            holder,
            new DateOnly(2024, 12, 31),
            null,
            new InstitutionPortfolioSummary { ReportedAum = 1, PositionCount = 1 },
            null,
            profile,
            null,
        ];

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

        deDeOutput.Should().Be(invariantOutput);
        invariantOutput
            .Should()
            .Contain(
                "| Form ADV adviser | CRD 1234, as of 2026-10-01, $5,000,000,000 regulatory assets |"
            )
            .And.Contain("(62.5% of regulatory assets)")
            .And.Contain("| Pooled investment vehicles | 43.8% of regulatory assets |")
            .And.Contain(
                "| Private funds | 1,234 funds, no fund type more than half, $9,876,543,210 gross assets |"
            )
            .And.Contain(
                "| Registered funds advised | 1,200 series (1,100 index, 1,050 ETF), $1,234,567,890 net assets, N-CEN as of 2026-06-30 |"
            );
    }
}
