using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderInstitutionSummaryNoPriorQuarterTests
{
    private static readonly MethodInfo RenderInstitutionSummaryMethod =
        typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderInstitutionSummary",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // RenderInstitutionSummary (extracted in #1547) takes a nullable previousDate
    // because a holder's earliest reported quarter has nothing to compare against.
    // The contract is: when previousDate is null, omit the "vs prior quarter …"
    // line entirely. A refactor that switched the nullable branch to render
    // previousDate.GetValueOrDefault() would silently emit "vs prior quarter
    // 0001-01-01" — meaningless to the LLM consumer.
    [Fact]
    public void RenderInstitutionSummary_PreviousDateNull_OmitsVsPriorQuarterLine()
    {
        var holder = new InstitutionalHolder { Name = "Test Fund" };
        var targetDate = new DateOnly(2024, 9, 30);
        var summary = new InstitutionPortfolioSummary
        {
            ReportedAum = 1_000_000_000L,
            PositionCount = 50,
            Top10ConcentrationPercent = 40.5,
            Top25ConcentrationPercent = 75.2,
            QoQTurnoverPercent = 12.3,
            QuartersReported = 1,
        };

        var rendered = (string)
            RenderInstitutionSummaryMethod.Invoke(
                null,
                [holder, targetDate, (DateOnly?)null, summary]
            );

        rendered.Should().Contain("Portfolio summary — **Test Fund** as of 2024-09-30");
        rendered.Should().NotContain("vs prior quarter");
    }
}
