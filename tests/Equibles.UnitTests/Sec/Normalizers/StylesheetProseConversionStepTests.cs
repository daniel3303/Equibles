using System.IO.Compression;
using System.Security.Cryptography;
using AngleSharp.Html.Parser;
using Equibles.Core.Documents;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.BusinessLogic.Normalizers;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec.Normalizers;

public class StylesheetProseConversionStepTests
{
    private const string Css = """
        .pf{position:relative}.pc{position:absolute;left:0px;top:0px}
        .t{position:absolute;white-space:pre;left:87px;font-size:36px;font-family:serif;
        transform:matrix(.375,0,0,.375,0,0);transform-origin:0 100%}
        .y0{bottom:166px}.y1{bottom:148px}.y2{bottom:130px}.y3{bottom:112px}
        """;
    private static readonly string[] Lines =
    [
        "On 20 October 2025, the Group entered into a three-year facility with Allied Irish Banks, plc, comprising a ",
        "term loan drawn to fund the acquisition of the subsidiary. The term loan bears interest at a fixed ",
        "margin over EURIBOR. Transaction costs incurred in connection with the debt facility have ",
        "been capitalised and are being amortised over the term of the facility. ",
    ];

    private static string Line(int index, string extra = "", string content = null) =>
        $"<div class='t y{index}' {extra}>{content ?? Lines[index]}</div>";

    private static string Paragraph() =>
        string.Concat(Enumerable.Range(0, 4).Select(index => Line(index)));

    private static string Markup(string content, string extraCss = "", string head = "") =>
        $"<html xmlns='http://www.w3.org/1999/xhtml'><head><style>{Css}{extraCss}</style>{head}</head>"
        + $"<body><div class='pf'><div class='pc'>{content}</div></div></body></html>";

    private static string Convert(string markup)
    {
        var document = new HtmlParser(
            new HtmlParserOptions { IsAcceptingCustomElementsEverywhere = true }
        ).ParseDocument(markup);
        StylesheetProseConversionStep.Execute(document, markup);
        return document.Body.InnerHtml;
    }

    [Fact]
    public void AStylesheetRunPreservesEveryCharacterAndPrintedLineBreak()
    {
        var result = new HtmlParser().ParseDocument(Convert(Markup(Paragraph())));
        result.QuerySelectorAll("p").Should().ContainSingle();
        result.QuerySelector("p").TextContent.Should().Be(string.Concat(Lines));
        result.QuerySelectorAll("br").Should().HaveCount(3);
    }

    [Fact]
    public void XmlEntitiesInStylesAndInlineXbrlRemainSourceText()
    {
        var markup = Markup(
            Line(0, content: "<span>" + Lines[0] + "</span>") + Line(1) + Line(2) + Line(3),
            ".t &gt; span{position:relative}"
        );
        Convert(markup).Should().Contain("<p>").And.Contain("<span>");
    }

    [Theory]
    [InlineData(".t{left:var(--unknown)}")]
    [InlineData(".t{left:20%}")]
    [InlineData(".t{top:0px}")]
    [InlineData(".y1{height:400px}")]
    [InlineData(".y1{line-height:500px}")]
    [InlineData(".y1{height:40px}")]
    [InlineData(".y1{line-height:1.5}")]
    [InlineData(".t{height:400px}")]
    [InlineData(".t{line-height:500px}")]
    [InlineData(".t{transform:rotate(10deg)}")]
    [InlineData(".t{translate:20px}")]
    [InlineData(".t{text-decoration:line-through}")]
    [InlineData(".t{word-spacing:500px}")]
    [InlineData(".t{white-space:normal}")]
    [InlineData(".pf{transform:rotate(10deg)}")]
    [InlineData(".pf{display:none}")]
    [InlineData(".pf{writing-mode:vertical-rl}")]
    [InlineData(".pf{direction:rtl}")]
    [InlineData(".pf{text-transform:uppercase}")]
    [InlineData(".pf{text-decoration-line:line-through}")]
    [InlineData(".t{unicode-bidi:isolate-override}")]
    [InlineData("@supports(display:grid){.t{left:200px}}")]
    [InlineData("@media(max-width:2000px){.t{left:200px}}")]
    [InlineData(".t:before{content:'additional terms'}")]
    [InlineData("@import url('https://example.test/style.css');")]
    public void UnsupportedOrHiddenLayoutRemainsUnjoined(string extraCss) =>
        Convert(Markup(Paragraph(), extraCss)).Should().NotContain("<p>");

    [Theory]
    [InlineData("transform:translateX(10px)")]
    [InlineData("position:absolute;left:50px")]
    [InlineData("font-size:200px")]
    [InlineData("word-spacing:500px")]
    [InlineData("white-space:normal")]
    [InlineData("height:200px")]
    [InlineData("line-height:500px")]
    [InlineData("text-decoration:line-through")]
    public void AChangedInlineLayoutCannotBecomeProse(string style) =>
        Convert(
                Markup(
                    Line(0)
                        + Line(1, content: $"<span style='{style}'>{Lines[1]}</span>")
                        + Line(2)
                        + Line(3)
                )
            )
            .Should()
            .NotContain("<p>");

    [Theory]
    [InlineData("\u202e")]
    [InlineData("\u200f")]
    [InlineData("\u05d0")]
    public void BidirectionalTextDoesNotAcquireAnAssumedVisualOrder(string text) =>
        Convert(
                Markup(
                    Line(0) + Line(1, content: text + Lines[1]) + Line(2) + Line(3),
                    ".t{unicode-bidi:bidi-override}"
                )
            )
            .Should()
            .NotContain("<p>");

    [Fact]
    public void CascadeAndImportantPositionsDetermineActualContinuity()
    {
        var content = Line(0) + Line(1, "style='left:87px'") + Line(2) + Line(3);
        Convert(Markup(content, ".y1{left:500px !important}")).Should().NotContain("<p>");
        Convert(Markup(content, ".y1{left:500px}")).Should().Contain("<p>");
    }

    [Theory]
    [InlineData("<div class='t y1' style='left:400px'>Separate column</div>")]
    [InlineData("<table><tr><td><div class='t y1' style='left:400px'>42.0</div></td></tr></table>")]
    [InlineData("<div class='t' style='bottom:auto'>Unknown position</div>")]
    [InlineData("<div><span style='position:absolute;left:400px;bottom:148px'>42.0</span></div>")]
    [InlineData("<div><span style='position:fixed;left:400px;bottom:148px'>42.0</span></div>")]
    [InlineData(
        "<div><span style='position:absolute;left:400px;bottom:200px;transform:translateY(52px)'>42.0</span></div>"
    )]
    public void OtherColumnsAndUnknownPositionsPreventAJoin(string neighbor) =>
        Convert(Markup(Paragraph() + neighbor)).Should().NotContain("<p>");

    [Fact]
    public void RejectedLinesCannotHidePositionedNeighboringAmounts()
    {
        var neighbor =
            "<div class='t' style='bottom:0px'>"
            + "<span style='position:absolute;left:300px;bottom:148px'>Separate neighboring table amount: 42.0</span></div>";
        Convert(Markup(Paragraph() + neighbor)).Should().NotContain("<p>");
    }

    [Fact]
    public void FlowBoundariesAndParagraphSpacingRemainIntact()
    {
        Convert(Markup(Line(0) + "Separate text" + Line(1) + Line(2))).Should().NotContain("<p>");
        Convert(Markup(Line(0) + "<table><tr><td>42</td></tr></table>" + Line(1) + Line(2)))
            .Should()
            .NotContain("<p>");
        Convert(Markup(Paragraph(), ".y2{bottom:112px}.y3{bottom:94px}"))
            .Should()
            .NotContain("<p>");
        Convert(Markup(Line(0) + Line(1))).Should().NotContain("<p>");
    }

    [Theory]
    [InlineData(50, 0, true)]
    [InlineData(120, 0, false)]
    [InlineData(50, 60, false)]
    public void TopAnchoredBlocksReserveTheirCompleteTextArea(int top, int rowTop, bool joins)
    {
        var table =
            $"<div style='position:absolute;left:300px;top:{top}px;height:50px'>"
            + "<span style='position:relative;height:50px'>"
            + $"<span style='position:absolute;left:0;top:{rowTop}px;font-size:13px;white-space:pre'>"
            + "A separate table value: 42.0</span></span></div>";
        var result = Convert(Markup(Paragraph() + table, ".pc{height:300px}"));
        result.Contains("<p>", StringComparison.Ordinal).Should().Be(joins);
        result.Should().Contain("A separate table value: 42.0");
    }

    [Fact]
    public void ATopBlockCannotHideMultiplePrintedLinesInsideOneCoordinate()
    {
        var start = "<div style='position:absolute;left:300px;top:50px;height:50px'>";
        var row = "<span style='position:absolute;left:0;top:0;font-size:13px;white-space:pre'>";
        var text = string.Join("&#10;", Enumerable.Repeat("A separate table value: 42.0", 12));
        Convert(Markup(Paragraph() + "<div class='t' style='bottom:0px'>" + text + "</div>"))
            .Should()
            .NotContain("<p>");
        Convert(Markup(Paragraph() + start + row + text + "</span></div>", ".pc{height:300px}"))
            .Should()
            .NotContain("<p>");
        Convert(
                Markup(
                    Paragraph()
                        + start
                        + "\n"
                        + row
                        + "A separate table value: 42.0</span>\n</div>",
                    ".pc{height:300px}"
                )
            )
            .Should()
            .Contain("<p>");
    }

    [Fact]
    public void ExternalStylesheetsAndInvalidXmlNeverSupplyGeometry()
    {
        Convert(
                Markup(
                    Paragraph(),
                    head: "<link rel='StyleSheet' href='https://example.test/layout.css' />"
                )
            )
            .Should()
            .NotContain("<p>");
        Convert(Markup(Paragraph()).Replace("</html>", "", StringComparison.Ordinal))
            .Should()
            .NotContain("<p>");
        Convert(
                Markup(Paragraph())
                    .Replace("xmlns='http://www.w3.org/1999/xhtml'", "", StringComparison.Ordinal)
            )
            .Should()
            .NotContain("<p>");
    }

    [Theory]
    [InlineData("media='(max-width:2000px)'")]
    [InlineData("title='alternate'")]
    [InlineData("type='application/json'")]
    [InlineData("scoped='scoped'")]
    public void UnsupportedStylesheetAttributesPreventAJoin(string attributes) =>
        Convert(Markup(Paragraph(), head: $"<style {attributes}>.t{{left:300px}}</style>"))
            .Should()
            .NotContain("<p>");

    [Fact]
    public void LaterStylesheetsAndActiveMediaKeepTheCssCascade()
    {
        Convert(Markup(Paragraph(), head: "<style media='screen'>.y1{left:300px}</style>"))
            .Should()
            .NotContain("<p>");
        Convert(Markup(Paragraph(), head: "<style media='print'>.y1{left:300px}</style>"))
            .Should()
            .Contain("<p>");
        Convert(Markup(Paragraph(), "@media screen{.y1{left:300px}}")).Should().NotContain("<p>");
        Convert(Markup(Paragraph(), "@media print{.y1{left:300px}}")).Should().Contain("<p>");
    }

    [Fact]
    public void ConditionalTransparentTextPaintCannotAlterGeometry()
    {
        const string media = "@media screen and (-webkit-min-device-pixel-ratio:0)";
        Convert(
                Markup(
                    Paragraph(),
                    media + "{.t{text-shadow:none;-webkit-text-stroke:0.015em transparent}}"
                )
            )
            .Should()
            .Contain("<p>");
        Convert(Markup(Paragraph(), media + "{.t{text-shadow:none;left:300px}}"))
            .Should()
            .NotContain("<p>");
    }

    [Fact]
    public void XmlAndStylesheetLimitsLeaveTheReportUnchanged()
    {
        Convert("<!DOCTYPE html [<!ENTITY extra 'external'>]>" + Markup(Paragraph()))
            .Should()
            .NotContain("<p>");
        Convert("<?layout href='https://example.test/layout.css'?>" + Markup(Paragraph()))
            .Should()
            .NotContain("<p>");
        Convert(
                Markup(
                    Paragraph(),
                    head: string.Concat(Enumerable.Repeat("<style>.x{color:red}</style>", 32))
                )
            )
            .Should()
            .NotContain("<p>");
        Convert(Markup(Paragraph(), "/*" + new string('x', 1024 * 1024) + "*/"))
            .Should()
            .NotContain("<p>");
        Convert(Markup(Paragraph(), string.Concat(Enumerable.Repeat(".x{color:red}", 20001))))
            .Should()
            .NotContain("<p>");
        Convert(Markup(Paragraph(), head: "<!--" + new string('x', 8 * 1024 * 1024) + "-->"))
            .Should()
            .NotContain("<p>");
        Convert(
                Markup(
                    Paragraph()
                        + string.Concat(Enumerable.Repeat("<div>", 130))
                        + string.Concat(Enumerable.Repeat("</div>", 130))
                )
            )
            .Should()
            .NotContain("<p>");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\t")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void OnePositionedLineCannotHideAdditionalLines(string separator) =>
        Convert(
                Markup(
                    Line(0)
                        + Line(1, content: Lines[1] + separator + "Another physical line")
                        + Line(2)
                        + Line(3)
                )
            )
            .Should()
            .NotContain("<p>");

    [Fact]
    public void JoiningNeverExposesHiddenSourceText()
    {
        Convert(Markup(Line(0) + Line(1, "hidden='hidden'") + Line(2) + Line(3)))
            .Should()
            .NotContain("<p>");
        Convert(
                Markup(
                    Line(0)
                        + Line(1, content: $"<span hidden='hidden'>{Lines[1]}</span>")
                        + Line(2)
                        + Line(3)
                )
            )
            .Should()
            .NotContain("<p>");
        Convert(
                Markup(Paragraph())
                    .Replace("class='pf'", "class='pf' hidden='hidden'", StringComparison.Ordinal)
            )
            .Should()
            .NotContain("<p>");
    }

    [Theory]
    [InlineData("inset-block-start:140px")]
    [InlineData("column-count:2")]
    [InlineData("line-height:500px")]
    public void UnknownTopBlockGeometryCannotBypassThePointInventory(string overrideCss)
    {
        var table =
            "<div style='position:absolute;left:300px;top:50px;height:50px'>"
            + $"<span style='position:absolute;left:0;top:0;font-size:13px;white-space:pre;{overrideCss}'>"
            + "A separate table value: 42.0</span></div>";
        Convert(Markup(Paragraph() + table, ".pc{height:300px}")).Should().NotContain("<p>");
    }

    [Fact]
    public void RecordedReportKeepsBorrowerAndFacilityParagraphsWithTheirOriginalWords()
    {
        // Two complete source pages and every stylesheet, with embedded font/image
        // payloads removed. No wording or positions were repaired in this fixture.
        using var stream = File.OpenRead(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Esef",
                "b2-impact-2025-styled-borrowings.xhtml.gz"
            )
        );
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        var bytes = output.ToArray();
        System
            .Convert.ToHexStringLower(SHA256.HashData(bytes))
            .Should()
            .Be("229d920016607503a1698ae6ae4db10f8b3b145a29bfb373b055018a8ac96a6a");
        var markup = XhtmlCompatibility.ExpandEmptyElements(
            System.Text.Encoding.UTF8.GetString(bytes)
        );
        var document = new HtmlParser(
            new HtmlParserOptions { IsAcceptingCustomElementsEverywhere = true }
        ).ParseDocument(markup);
        var table = document
            .QuerySelectorAll(".pc > div")
            .Single(element =>
                !element.ClassList.Contains("t")
                && element.TextContent.Contains("Audit fees", StringComparison.Ordinal)
            );
        var originalTable = table.OuterHtml;
        StylesheetProseConversionStep.Execute(document, markup);
        table.OuterHtml.Should().Be(originalTable);
        var normalized = new SecDocumentHtmlNormalizer().NormalizeFragment(markup);
        var paragraphs = new HtmlParser().ParseDocument(normalized).QuerySelectorAll("p");
        var borrower = paragraphs.Single(paragraph =>
            paragraph.TextContent.StartsWith(
                "B2 Impact ASA has issued a guarantee",
                StringComparison.Ordinal
            )
        );
        borrower.QuerySelectorAll("br").Should().HaveCount(5);
        borrower
            .TextContent.Should()
            .Contain("B2Kapital Holding S.à r.l.")
            .And.Contain("EUR 610 million")
            .And.Contain("facilities at 31 December 2025 was EUR 284 million.");
        var loan = paragraphs.Single(paragraph =>
            paragraph.TextContent.StartsWith("The Group is financed", StringComparison.Ordinal)
        );
        loan.QuerySelectorAll("br").Should().HaveCount(3);
        loan.TextContent.Should().Contain("agreement (RCF)").And.Contain("January 2031");
        var text = System.Text.Encoding.UTF8.GetString(
            EsefReportContent.Build(
                System.Text.Encoding.UTF8.GetString(bytes),
                new SecDocumentHtmlNormalizer(),
                new SecDocumentHtmlToMarkdownConverter()
            )
        );
        text.Split("\n\n")
            .Single(block =>
                block.StartsWith("B2 Impact ASA has issued a guarantee", StringComparison.Ordinal)
            )
            .Should()
            .Contain("B2Kapital Holding S.à r.l.")
            .And.Contain("EUR 610 million")
            .And.Contain("EUR 284 million");
        text.Should().Contain("Audit fees").And.Contain("Fees for tax advise");
    }
}
