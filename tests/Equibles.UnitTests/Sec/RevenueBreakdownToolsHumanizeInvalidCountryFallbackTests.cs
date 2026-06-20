using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Contract (RevenueBreakdownTools.cs country arm): a "country:" member resolves to its English
/// region name, but an unrecognised code must fall back to the raw local part rather than throw —
/// the RegionInfo constructor raises ArgumentException for a non-ISO code and the catch returns
/// `local`. Asserting the fallback (not a valid code's EnglishName) keeps the test independent of
/// the ICU version, which renders country names differently across platforms.
/// </summary>
public class RevenueBreakdownToolsHumanizeInvalidCountryFallbackTests
{
    [Fact]
    public void Humanize_CountryQNameWithInvalidRegionCode_FallsBackToLocalName()
    {
        var result = RevenueBreakdownTools.Humanize("country:Atlantis");

        result
            .Should()
            .Be("Atlantis", "an unrecognised region code falls back to the raw local name");
    }
}
