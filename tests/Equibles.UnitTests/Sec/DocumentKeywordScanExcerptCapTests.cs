using Equibles.Sec.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

// SearchDocument's exact mode honours maxExcerptChars: an annual report whose lines are whole paragraphs printed
// thousands of characters per match (each match plus a context line on each side), whatever the caller asked for.
public class DocumentKeywordScanExcerptCapTests
{
    private static readonly string Filler = string.Join(
        ' ',
        Enumerable.Repeat("lorem ipsum dolor", 120)
    );

    private static readonly string[] Paragraphs =
    [
        "Board report opening. " + Filler + " The closing words of the paragraph before.",
        Filler
            + " For 2026 the Company targets around 870 commercial aircraft deliveries. "
            + Filler,
        "The opening words of the paragraph after. " + Filler,
    ];

    [Fact]
    public void ACappedScan_CutsEachLineNearTheMatch_AndLinksTheFullLine()
    {
        var builder = new DocumentKeywordScanBlockLinkTests.RecordingLinkBuilder();

        var rendered = DocumentKeywordScanBlockLinkTests.Render(
            Paragraphs,
            [1],
            "870",
            builder,
            maxExcerptChars: 300
        );

        var printed = rendered.Split('\n').Where(line => line.Contains('│')).ToList();
        printed.Should().HaveCount(3);
        printed.Should().AllSatisfy(line => line.Length.Should().BeLessThan(340));
        printed[0].Should().Contain("…").And.Contain("The closing words of the paragraph before.");
        printed[1]
            .Should()
            .Contain("around **870** commercial aircraft deliveries")
            .And.StartWith("     2 │ …");
        printed[2].Should().Contain("The opening words of the paragraph after.").And.EndWith("…");
        builder.Calls.Should().ContainSingle().Which.ExcerptText.Should().Be(Paragraphs[1]);
    }

    [Fact]
    public void AnUncappedScan_PrintsWholeLines()
    {
        var capped = DocumentKeywordScanBlockLinkTests.Render(
            Paragraphs,
            [1],
            "870",
            null,
            maxExcerptChars: 0
        );

        capped.Should().Contain(Paragraphs[0]).And.Contain(Paragraphs[2]).And.NotContain("…");
    }

    [Fact]
    public void AQueryLongerThanTheCap_IsShownWhole()
    {
        const string phrase = "around 870 commercial aircraft deliveries";

        var clipped = DocumentKeywordScan.ClipAroundMatch(Paragraphs[1], phrase, 20);

        clipped.Should().Be("…" + phrase + "…");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AMatchNearAnEnd_StaysInTheWindow(bool atStart)
    {
        var words = string.Join(' ', Enumerable.Repeat("word", 400));
        var line = atStart ? "870 opens the line " + words : words + " ends with 870";

        var clipped = DocumentKeywordScan.ClipAroundMatch(line, "870", 120);

        clipped.Should().Contain("870");
        clipped.Length.Should().BeLessThanOrEqualTo(122);
        (atStart ? clipped.EndsWith('…') : clipped.StartsWith('…')).Should().BeTrue();
    }
}
