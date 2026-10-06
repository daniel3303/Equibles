using System.IO.Compression;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.BusinessLogic.Normalizers;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec.Normalizers;

public class PositionedProseConversionStepTests
{
    private static readonly string[] Lines =
    [
        "On 20 October 2025, the Group entered into a three-year facility with Allied Irish Banks, plc (“AIB”), comprising a ",
        "€10.3 million term loan drawn to fund the acquisition of OccasionGenius Inc. The term loan bears interest at a fixed ",
        "margin of 2.2% over EURIBOR. Transaction costs of €0.1 million incurred in connection with the debt facility have ",
        "been capitalised and are being amortised over the term of the facility. ",
    ];

    private static string Line(
        int index,
        double left = 87,
        double? bottom = null,
        string text = null
    ) =>
        FormattableString.Invariant(
            $"<div class='t' style='left:{left}px;bottom:{bottom ?? 166 - 18 * index}px;display:inline'>{text ?? Lines[index]}</div>"
        );

    private static string Paragraph() =>
        string.Concat(Enumerable.Range(0, Lines.Length).Select(index => Line(index)));

    private static string Convert(string content, bool page = true)
    {
        var document = new HtmlParser(
            new HtmlParserOptions { IsAcceptingCustomElementsEverywhere = true }
        ).ParseDocument(page ? $"<div class='DTRTextContainer'>{content}</div>" : content);
        new PositionedProseConversionStep().Execute(document);
        return document.Body.InnerHtml;
    }

    [Fact]
    public void PrintedProseLinesBecomeOneParagraphWithExplicitLineBreaks()
    {
        var document = new HtmlParser().ParseDocument(Convert(Paragraph()));
        document.QuerySelectorAll("p").Should().HaveCount(1);
        document.QuerySelectorAll("br").Should().HaveCount(3);
        document.QuerySelector("p").TextContent.Should().Be(string.Concat(Lines));
    }

    [Fact]
    public void RealReportPageKeepsTheTableAndReconstructsTheBorrowingParagraph()
    {
        using var file = File.OpenRead(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Esef",
                "hostelworld-2025-borrowings-page.xhtml.gz"
            )
        );
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var source = reader.ReadToEnd();
        var normalizer = new SecDocumentHtmlNormalizer();
        var normalized = normalizer.NormalizeFragment(source);
        var document = new HtmlParser().ParseDocument(normalized);
        var paragraph = document
            .QuerySelectorAll("p")
            .Single(element =>
                element.TextContent.StartsWith("On 20 October 2025", StringComparison.Ordinal)
            );
        paragraph.TextContent.Should().Be(string.Concat(Lines));
        paragraph.QuerySelectorAll("br").Should().HaveCount(3);
        document.QuerySelectorAll("table").Should().NotBeEmpty();
        var text = new SecDocumentHtmlToMarkdownConverter().Convert(normalized);
        text.Should().Contain("| Drawdown | 10.3 | – |");
        text.Should()
            .Contain("comprising a")
            .And.Contain("€10.3 million term loan")
            .And.Contain("margin of 2.2% over EURIBOR");
        var loan = text[text.IndexOf("On 20 October 2025", StringComparison.Ordinal)..];
        loan[..loan.IndexOf("been capitalised", StringComparison.Ordinal)]
            .Should()
            .NotContain("\n\n");
    }

    [Fact]
    public void NeighboringColumnsAtTheSameBaselinesRemainSeparate()
    {
        var columns = string.Concat(
            Enumerable.Range(0, Lines.Length).Select(index => Line(index, 400))
        );
        Convert(Paragraph() + columns).Should().NotContain("<p>");
    }

    [Fact]
    public void StaggeredColumnsCannotBecomeOneRun()
    {
        Convert(Line(0) + Line(1, 400) + Line(2) + Line(3, 400)).Should().NotContain("<p>");
    }

    [Fact]
    public void TablesAndListsKeepTheirOwnBoundaries()
    {
        Convert($"<table><tr><td>{Paragraph()}</td></tr></table><ul><li>{Paragraph()}</li></ul>")
            .Should()
            .NotContain("<p>");
    }

    [Fact]
    public void AParallelTableCellMakesItsBaselineAmbiguous()
    {
        Convert(Paragraph() + $"<table><tr><td>{Line(1, 400, text: "10.3")}</td></tr></table>")
            .Should()
            .NotContain("<p>");
    }

    [Theory]
    [InlineData(120)]
    [InlineData(155)]
    [InlineData(166)]
    [InlineData(190)]
    public void IrregularOrReversedSpacingDoesNotJoinThreeLines(double middle)
    {
        Convert(Line(0) + Line(1, bottom: middle) + Line(2)).Should().NotContain("<p>");
    }

    [Fact]
    public void ParagraphGapAndHeadingStaySeparate()
    {
        var source = Line(0) + Line(1) + Line(2, bottom: 111) + Line(3, bottom: 93);
        Convert(source).Should().NotContain("<p>");
        Convert(Line(0, text: "BORROWINGS ") + Line(1) + Line(2)).Should().NotContain("<p>");
    }

    [Theory]
    [InlineData(".")]
    [InlineData(":")]
    [InlineData(";")]
    [InlineData("?")]
    public void EndedSentencesAndListIntroductionsRemainBoundaries(string punctuation)
    {
        Convert(Line(0, text: Lines[0].TrimEnd() + punctuation) + Line(1) + Line(2))
            .Should()
            .NotContain("<p>");
    }

    [Fact]
    public void ANewCapitalizedParagraphIsNotAContinuation()
    {
        Convert(
                Line(0)
                    + Line(1, text: "Another separate paragraph starts with different contractual terms ")
                    + Line(2)
            )
            .Should()
            .NotContain("<p>");
    }

    [Fact]
    public void LongCapitalizedHeadingsAndNumericRowsStaySeparate()
    {
        Convert(
                Line(0, text: "LONG TERM BORROWINGS AND OTHER FINANCIAL LIABILITIES ")
                    + Line(1)
                    + Line(2)
            )
            .Should()
            .NotContain("<p>");
        Convert(
                Line(0)
                    + Line(1, text: "2025 liabilities and commitments for the current period ")
                    + Line(2)
            )
            .Should()
            .NotContain("<p>");
    }

    [Fact]
    public void UnknownPositionedLineElementPreventsPageReconstruction()
    {
        Convert(Paragraph() + "<span class='t' style='left:400px;bottom:148px'>Other column</span>")
            .Should()
            .NotContain("<p>");
    }

    [Fact]
    public void SeparateXbrlDefinitionsAreNotMerged()
    {
        Convert(
                $"<ix:continuation>{Line(0)}{Line(1)}</ix:continuation><ix:continuation>{Line(2)}{Line(3)}</ix:continuation>"
            )
            .Should()
            .NotContain("<p>");
    }

    [Theory]
    [InlineData("left:87px")]
    [InlineData("left:87%;bottom:166px")]
    [InlineData("left:87px;bottom:166px;bottom:150px")]
    [InlineData("left:87px;bottom:NaNpx")]
    public void AnUnpositionedNeighborCannotBeIgnored(string style)
    {
        Convert(Paragraph() + $"<div class='t' style='{style}'>Other text</div>")
            .Should()
            .NotContain("<p>");
    }

    [Fact]
    public void TwoLinesAndUnrecognizedPageLayoutsStayUnchanged()
    {
        Convert(Line(0) + Line(1)).Should().NotContain("<p>");
        Convert(Paragraph(), page: false).Should().NotContain("<p>");
    }

    [Fact]
    public void VisibleInterveningNodesPreventAJoin()
    {
        Convert(Line(0) + "separate text" + Line(1) + Line(2)).Should().NotContain("<p>");
        Convert(Line(0) + "<hr>" + Line(1) + Line(2)).Should().NotContain("<p>");
    }

    [Theory]
    [InlineData("div")]
    [InlineData("span")]
    public void APositionedNeighborWithoutTheLineClassStillBlocksJoining(string tag)
    {
        Convert(Paragraph() + $"<{tag} style='left:400px;bottom:148px'>Other column</{tag}>")
            .Should()
            .NotContain("<p>");
    }

    [Theory]
    [InlineData("transform:translateY(200px)")]
    [InlineData("translate:0 200px")]
    [InlineData("top:10px;height:18px;position:absolute")]
    [InlineData("page-break-before:always")]
    [InlineData("break-before:page")]
    [InlineData("margin-bottom:200px")]
    [InlineData("zoom:2")]
    [InlineData("right:0")]
    public void CoordinateOverridesAndExplicitBreaksPreventReconstruction(string declarations)
    {
        var second = Line(1).Replace("display:inline", $"display:inline;{declarations}");
        Convert(Line(0) + second + Line(2) + Line(3)).Should().NotContain("<p>");
    }

    [Fact]
    public void NestedPositioningAndStylesheetsLeaveThePageUnchanged()
    {
        Convert($"<div style='left:200px;bottom:100px'>{Paragraph()}</div>")
            .Should()
            .NotContain("<p>");
        Convert("<style>.t { transform:translateY(100px) }</style>" + Paragraph())
            .Should()
            .NotContain("<p>");
        Convert("<link rel='stylesheet' href='layout.css'>" + Paragraph())
            .Should()
            .NotContain("<p>");
    }

    [Fact]
    public void ClassDrivenListsKeepEveryItemInTheCompletePipeline()
    {
        var source =
            $"<div class='DTRTextContainer'>{Paragraph().Replace("class='t'", "class='t item-list-element-wrapper'")}</div>";
        var normalized = new SecDocumentHtmlNormalizer().NormalizeFragment(source);
        var document = new HtmlParser().ParseDocument(normalized);
        document.QuerySelectorAll("li").Should().HaveCount(4);
        document.QuerySelectorAll("p").Should().BeEmpty();
    }

    [Fact]
    public void AStyledHeadingCannotAbsorbSubsequentProse()
    {
        var heading = Line(0, text: $"<span style='font-weight:bold'>{Lines[0]}</span>");
        var source = $"<div class='DTRTextContainer'>{heading}{Line(1)}{Line(2)}{Line(3)}</div>";
        var normalized = new SecDocumentHtmlNormalizer().NormalizeFragment(source);
        var document = new HtmlParser().ParseDocument(normalized);
        document.QuerySelector("h3").TextContent.Should().Be(Lines[0].Trim());
        document.QuerySelectorAll("p").Should().BeEmpty();
        document.Body.TextContent.Should().Contain(Lines[1]).And.Contain(Lines[2]);
    }

    [Fact]
    public void OversizedCompactionCannotSupplyMissingGeometry()
    {
        var padding = new string('0', EsefReportContent.MaxRetrievalHtmlChars);
        var source =
            $"<html xmlns='http://www.w3.org/1999/xhtml'><body><div class='DTRTextContainer' style='font-size:{padding}px'>{Paragraph()}</div></body></html>";
        var compact = EsefReportContent.PrepareRetrievalMarkup(source);
        compact.Length.Should().BeLessThan(EsefReportContent.MaxRetrievalHtmlChars);
        var normalized = new SecDocumentHtmlNormalizer().NormalizeFragment(compact);
        var document = new HtmlParser().ParseDocument(normalized);
        document.QuerySelectorAll("p").Should().BeEmpty();
        foreach (var line in Lines)
            document.Body.TextContent.Should().Contain(line);
    }

    [Fact]
    public void ConversionIsIdempotent()
    {
        var first = Convert(Paragraph());
        Convert(first, page: false).Should().Be(first);
    }
}
