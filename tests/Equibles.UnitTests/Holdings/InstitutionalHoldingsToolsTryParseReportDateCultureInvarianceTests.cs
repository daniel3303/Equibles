using System.Globalization;
using System.Reflection;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsTryParseReportDateCultureInvarianceTests
{
    // TryParseReportDate parses the LLM-supplied `reportDate` MCP argument via
    // DateOnly.TryParse(input, out result) with no InvariantCulture. The
    // tool's reportDate is an ISO yyyy-MM-dd quarter end (e.g. "2024-06-30"),
    // so it must resolve identically on any host — the sibling McpToolExecutor
    // date parsing was already moved to InvariantCulture (#2661). Under th-TH
    // (ThaiBuddhist, +543-year era) the ambient parser reads "2024" as a
    // Buddhist-era year (-> 1481 CE); the parsed date then fails the
    // validDates Contains() check in ResolveReportDate, so the caller's
    // explicit quarter selection is silently dropped to the latest filing.
    [Fact(Skip = "GH-2677 — reportDate MCP arg parsed with host culture, not InvariantCulture")]
    public void TryParseReportDate_IsoQuarterEndUnderThaiBuddhistCulture_ResolvesGregorianDate()
    {
        var method = typeof(InstitutionalHoldingsTools).GetMethod(
            "TryParseReportDate",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("th-TH");
        try
        {
            var args = new object[] { "2024-06-30", null };
            var parsed = (bool)method!.Invoke(null, args);

            parsed.Should().BeTrue();
            ((DateOnly)args[1]).Should().Be(new DateOnly(2024, 6, 30));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
