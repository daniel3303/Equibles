using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

// TryBuildAsFiledHtml stitches a filing's primary document together with its displayable
// exhibits so the as-filed viewer can show the WHOLE 8-K — cover page PLUS the Exhibit 99.1
// press release — and rewrites the cover page's intra-filing exhibit links to in-page anchors.
public class SecDocumentEnvelopeParserAsFiledHtmlTests
{
    private const string PrimaryBody =
        "<html><body><p>Item 9.01 Financial Statements and Exhibits</p>"
        + "<a href=\"acme_ex99-1.htm\">Press release issued by Acme, Inc.</a></body></html>";

    private const string ExhibitBody =
        "<html><body><p>Acme reported revenue of $300M for the quarter.</p></body></html>";

    private static string Submission(string primaryFileName, bool withExhibit)
    {
        var exhibitBlock = withExhibit
            ? $"""
                <DOCUMENT>
                <TYPE>EX-99.1
                <SEQUENCE>2
                <FILENAME>acme_ex99-1.htm
                <TEXT>
                {ExhibitBody}
                </TEXT>
                </DOCUMENT>
                """
            : string.Empty;

        return $"""
            <SEC-DOCUMENT>
            <DOCUMENT>
            <TYPE>8-K
            <SEQUENCE>1
            <FILENAME>{primaryFileName}
            <TEXT>
            {PrimaryBody}
            </TEXT>
            </DOCUMENT>
            {exhibitBlock}
            </SEC-DOCUMENT>
            """;
    }

    [Fact]
    public void TryBuildAsFiledHtml_PrimaryWithExhibit_StitchesBothInAnchoredSections()
    {
        var envelope = Submission("acme_8k.htm", withExhibit: true);

        var built = SecDocumentEnvelopeParser.TryBuildAsFiledHtml(
            envelope,
            "acme_8k.htm",
            out var content
        );

        built.Should().BeTrue();
        // Both documents are present, each in its own anchored section.
        content.Should().Contain("Item 9.01 Financial Statements and Exhibits");
        content.Should().Contain("Acme reported revenue of $300M for the quarter.");
        content.Should().Contain("id=\"asfiled-0\"");
        content.Should().Contain("id=\"asfiled-1\"");
    }

    [Fact]
    public void TryBuildAsFiledHtml_RewritesIntraFilingExhibitLinkToInPageAnchor()
    {
        var envelope = Submission("acme_8k.htm", withExhibit: true);

        SecDocumentEnvelopeParser.TryBuildAsFiledHtml(envelope, "acme_8k.htm", out var content);

        // The cover page's link to the exhibit file becomes a fragment to the exhibit's section,
        // so it scrolls in-page instead of pointing at a file we don't host.
        content.Should().Contain("href=\"#asfiled-1\"");
        content.Should().NotContain("href=\"acme_ex99-1.htm\"");
    }

    [Fact]
    public void TryBuildAsFiledHtml_PrimaryNameBlank_TreatsFirstBlockAsPrimary()
    {
        // The backfill re-fetches by accession and doesn't know the primary's filename; the
        // first displayable block (the primary by EDGAR convention) must still lead and the
        // exhibit link still resolve.
        var envelope = Submission("acme_8k.htm", withExhibit: true);

        var built = SecDocumentEnvelopeParser.TryBuildAsFiledHtml(
            envelope,
            primaryDocumentFileName: "",
            out var content
        );

        built.Should().BeTrue();
        content.Should().Contain("href=\"#asfiled-1\"");
        content.Should().Contain("Acme reported revenue of $300M for the quarter.");
    }

    [Fact]
    public void TryBuildAsFiledHtml_NoExhibit_ReturnsFalse()
    {
        var envelope = Submission("acme_8k.htm", withExhibit: false);

        SecDocumentEnvelopeParser
            .TryBuildAsFiledHtml(envelope, "acme_8k.htm", out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void TryBuildAsFiledHtml_EmptyEnvelope_ReturnsFalse()
    {
        SecDocumentEnvelopeParser
            .TryBuildAsFiledHtml(string.Empty, "acme_8k.htm", out _)
            .Should()
            .BeFalse();
    }
}
