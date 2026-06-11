using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Contract (RevenueBreakdownTools.cs:221-223): the "everything else" branch
/// of Humanize drops the "Member" suffix and spaces the PascalCase local
/// name. A non-country QName with three PascalCase words and no "Member"
/// suffix is the diagnostic input — it exercises the regex (lower→upper
/// boundaries) without crossing the "country:" or "Member"-strip arms,
/// so a regression in either side is isolated to this test.
/// </summary>
public class RevenueBreakdownToolsHumanizePascalCaseSpacingTests
{
    [Fact]
    public void Humanize_NonCountryQNameWithoutMemberSuffix_SpacesPascalCaseLocalName()
    {
        var result = RevenueBreakdownTools.Humanize("us-gaap:StatementGeographicalAxis");

        result.Should().Be("Statement Geographical Axis");
    }
}
