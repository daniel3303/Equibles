using Equibles.Sec.HostedService.Services;
using FluentAssertions;

namespace Equibles.UnitTests.Esef;

public class EsefStreamedTextTests
{
    private const string Xhtml = "http://www.w3.org/1999/xhtml";

    private static string Read(string body) =>
        EsefStreamedText.Read(
            $"<html xmlns=\"{Xhtml}\" xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\"><head><title>Hidden title</title></head><body>{body}</body></html>",
            1024 * 1024
        );

    [Fact]
    public void Read_ParagraphsHeadingsAndBreaks_BecomeLines()
    {
        Read(
                "<h2>Risk  management</h2><p>Liquidity is\n monitored daily.</p><div>Line one<br/>Line two</div>"
            )
            .Should()
            .Be("## Risk management\n\nLiquidity is monitored daily.\n\nLine one\nLine two");
    }

    [Fact]
    public void Read_EmptySpansBetweenSyllables_JoinTheWord()
    {
        Read("<p>Bas<span class=\"_ _0\"/>is of p<span class=\"_ _0\"/>reparation</p>")
            .Should()
            .Be("Basis of preparation");
    }

    [Fact]
    public void Read_Table_BecomesRowsUnderAHeaderRule_WithNestedTablesAsCellText()
    {
        Read(
                "<p>Borrowings</p><table><tr><td>Facility</td><td>2025</td></tr>"
                    + "<tr><td>Term <b>loan</b> A|B</td><td><table><tr><td>104</td><td>301</td></tr></table></td></tr>"
                    + "<tr><td> </td><td></td></tr></table><p>After</p>"
            )
            .Should()
            .Be(
                "Borrowings\n\n| Facility | 2025 |\n| --- | --- |\n| Term loan A\\|B | 104 301 |\n\nAfter"
            );
    }

    [Theory]
    [InlineData("<script>var hidden = 1;</script>")]
    [InlineData("<style>p {color:red}</style>")]
    [InlineData("<div style=\"display: none\">Hidden fact</div>")]
    [InlineData(
        "<ix:header><ix:hidden><ix:nonNumeric name=\"x\" contextRef=\"c\">Hidden fact</ix:nonNumeric></ix:hidden></ix:header>"
    )]
    public void Read_ContentNoReaderSees_IsLeftOut(string hidden)
    {
        Read($"<p>Before</p>{hidden}<p>After</p>").Should().Be("Before\n\nAfter");
    }

    [Fact]
    public void Read_TaggedFactsInTheBody_KeepTheirText()
    {
        Read(
                "<p>Assets of <ix:nonFraction name=\"ifrs-full:Assets\" contextRef=\"c\">5 708</ix:nonFraction> million</p>"
            )
            .Should()
            .Be("Assets of 5 708 million");
    }

    [Theory]
    [InlineData("<html><body><p>Not XHTML</p></body></html>")]
    [InlineData("<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>No root</p></div>")]
    [InlineData("<html xmlns=\"http://www.w3.org/1999/xhtml\"><body><p>Unclosed</body></html>")]
    public void Read_MarkupThatIsNotWellFormedXhtml_GivesNoText(string source)
    {
        EsefStreamedText.Read(source, 1024).Should().BeNull();
    }

    [Fact]
    public void Read_TextPastTheLimit_GivesNoTextRatherThanAPrefix()
    {
        var body = string.Concat(Enumerable.Repeat("<p>Ten chars</p>", 20));

        EsefStreamedText
            .Read($"<html xmlns=\"{Xhtml}\"><body>{body}</body></html>", 100)
            .Should()
            .BeNull();
    }

    // A drawing's words stay, as the conversion keeps them, and its stylesheet and script do not.
    [Fact]
    public void Read_InlineDrawing_KeepsItsWordsWithoutItsCode()
    {
        Read(
                "<p>Before chart</p><svg xmlns=\"http://www.w3.org/2000/svg\"><style>.cls-1{fill:#003b5c}</style>"
                    + "<script>var shown = 1;</script><g style=\"display: none\"><text>Draft layer</text></g>"
                    + "<text>2025</text><text>2024</text></svg><p>After chart</p>"
            )
            .Should()
            .Be("Before chart\n\n20252024\nAfter chart");
    }

    // A caption is a paragraph before the table's rows, as the conversion writes it.
    [Fact]
    public void Read_TableCaption_IsAParagraphBeforeTheRows()
    {
        Read(
                "<table><caption>Borrowings by maturity</caption><tr><td>2025</td><td>104</td></tr></table>"
            )
            .Should()
            .Be("Borrowings by maturity\n\n| 2025 | 104 |\n| --- | --- |");
    }
}
