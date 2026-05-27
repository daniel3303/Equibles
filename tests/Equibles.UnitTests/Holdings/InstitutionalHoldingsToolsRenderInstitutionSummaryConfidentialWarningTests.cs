using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderInstitutionSummaryConfidentialWarningTests
{
    // SEC 13F filers can request confidential treatment for a position, which
    // SEC delays publication of for up to one year. RenderInstitutionSummary
    // surfaces this state to the LLM consumer with an explicit warning so the
    // "portfolio may be incomplete" caveat travels with the data — without it,
    // an LLM would treat a partial portfolio as authoritative. A refactor
    // that negated the boolean check (or hoisted the block above
    // ConfidentialTreatmentRequested mid-cleanup) would silently drop the
    // warning for the very filers it's designed for.
    [Fact]
    public void RenderInstitutionSummary_HolderWithConfidentialTreatment_OutputContainsWarning()
    {
        var method = typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderInstitutionSummary",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var holder = new InstitutionalHolder
        {
            Name = "Bridgewater Associates",
            Cik = "0001350694",
            ConfidentialTreatmentRequested = true,
        };
        var summary = new InstitutionPortfolioSummary
        {
            ReportedAum = 100_000_000L,
            PositionCount = 50,
            Top10ConcentrationPercent = 40.0,
            Top25ConcentrationPercent = 70.0,
            QoQTurnoverPercent = 15.0,
            QuartersReported = 8,
        };

        var rendered = (string)
            method.Invoke(
                null,
                [holder, new DateOnly(2024, 9, 30), (DateOnly?)new DateOnly(2024, 6, 30), summary]
            );

        rendered.Should().Contain("Confidential Treatment");
    }
}
