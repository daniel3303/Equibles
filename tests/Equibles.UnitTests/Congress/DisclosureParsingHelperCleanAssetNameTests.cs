using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

// CleanAssetName strips the PDF checkbox artifact from House asset names. The House disclosure
// PDFs draw each row's checkboxes with a symbol font whose glyphs extract as the letter runs
// "gfedc" / "gfedcb"; the text extractor glues those onto the asset name, and ~9k production
// rows shipped like "Weyerhaeuser Company (WY) gfedcb" before this cleanup existed. The strip
// must be word-bounded and case-sensitive so real asset-name words are never eaten, and must
// collapse the whitespace the removal leaves so names don't keep double spaces.
public class DisclosureParsingHelperCleanAssetNameTests
{
    [Theory]
    [InlineData("Weyerhaeuser Company (WY) gfedcb", "Weyerhaeuser Company (WY)")]
    [InlineData("Kimco Realty Corporation (KIM)  gfedc", "Kimco Realty Corporation (KIM)")]
    [InlineData(
        "International Business Machines gfedc Corporation (IBM)",
        "International Business Machines Corporation (IBM)"
    )]
    [InlineData("Starbucks Corporation (SBuX) gfedcb", "Starbucks Corporation (SBuX)")]
    public void CleanAssetName_StripsCheckboxArtifacts(string raw, string expected)
    {
        DisclosureParsingHelper.CleanAssetName(raw).Should().Be(expected);
    }

    [Fact]
    public void CleanAssetName_CleanName_IsUnchanged()
    {
        DisclosureParsingHelper
            .CleanAssetName("Apple Inc. (AAPL)")
            .Should()
            .Be("Apple Inc. (AAPL)");
    }

    // Word-bounded and case-sensitive: the artifact letters inside a real word, or uppercased,
    // must never be eaten.
    [Theory]
    [InlineData("Gfedc Industries (GF)")]
    [InlineData("Kingfedcorp Holdings (KFC)")]
    public void CleanAssetName_NeverEatsRealWords(string name)
    {
        DisclosureParsingHelper.CleanAssetName(name).Should().Be(name);
    }

    [Fact]
    public void CleanAssetName_NullOrEmpty_PassesThrough()
    {
        DisclosureParsingHelper.CleanAssetName(null).Should().BeNull();
        DisclosureParsingHelper.CleanAssetName("").Should().Be("");
    }
}
