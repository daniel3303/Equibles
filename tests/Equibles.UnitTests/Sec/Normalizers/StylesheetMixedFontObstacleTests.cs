using System.IO.Compression;
using System.Security.Cryptography;
using AngleSharp.Html.Parser;
using Equibles.Core.Documents;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class StylesheetMixedFontObstacleTests
{
    private const string Css = """
        .pf{position:relative}.pc{position:absolute;left:0px;top:0px}
        .t{position:absolute;white-space:pre;left:87px;font-size:36px;font-family:serif;
        transform:matrix(.375,0,0,.375,0,0);transform-origin:0 100%;line-height:1.1}
        """;
    private const string Paragraph = """
        <div class='t' style='bottom:166px'>The Company has issued guarantees towards the lessor of the two vessels on behalf of</div><div class='t' style='bottom:148px'>the lessees Alpha Shipping AS and Beta Shipping AS respectively, see note 11. No guarantee</div><div class='t' style='bottom:130px'>commissions are made related to the current leases.</div>
        """;

    private static string Markup(string neighbor, string extraCss = "") =>
        $"<html xmlns='http://www.w3.org/1999/xhtml'><head><style>{Css}{extraCss}</style></head>"
        + $"<body><div class='pf'><div class='pc'>{Paragraph}{neighbor}</div></div></body></html>";

    private static string NumericRow(string style, int bottom = 0) =>
        $"<div class='t number' style='bottom:{bottom}px;left:400px'><span style='{style}'>19,623</span></div>";

    private static AngleSharp.Html.Dom.IHtmlDocument Convert(string markup)
    {
        var document = new HtmlParser(
            new HtmlParserOptions { IsAcceptingCustomElementsEverywhere = true }
        ).ParseDocument(markup);
        StylesheetProseConversionStep.Execute(document, markup);
        return document;
    }

    [Theory]
    [InlineData("font-family:sans-serif;line-height:1.057129")]
    [InlineData("font-weight:bold")]
    [InlineData("font-style:italic")]
    [InlineData("line-height:1.5")]
    [InlineData("line-height:54px")]
    public void ASeparateBoundedNumericRowKeepsItsTextAndAllowsTheProse(string style)
    {
        using var document = Convert(Markup(NumericRow(style)));
        document.QuerySelectorAll("p").Should().ContainSingle();
        document
            .QuerySelector("p")
            .TextContent.Should()
            .Contain("Alpha Shipping AS and Beta Shipping AS");
        document.QuerySelector(".number").TextContent.Should().Be("19,623");
        document.QuerySelector(".number").ParentElement.LocalName.Should().Be("div");
        document.QuerySelectorAll("p br").Should().HaveCount(2);
    }

    [Theory]
    [InlineData(130)]
    [InlineData(148)]
    [InlineData(166)]
    [InlineData(110)]
    [InlineData(195)]
    public void OverlappingNumericLineBoxesStillPreventAJoin(int bottom)
    {
        using var document = Convert(
            Markup(NumericRow("font-family:sans-serif;line-height:1.057129", bottom))
        );
        document.QuerySelector("p").Should().BeNull();
    }

    [Theory]
    [InlineData("font-size:72px")]
    [InlineData("line-height:500px")]
    [InlineData("line-height:3")]
    [InlineData("line-height:0")]
    [InlineData("top:130px")]
    [InlineData("position:absolute;bottom:148px")]
    [InlineData("transform:translateY(-148px)")]
    [InlineData("visibility:hidden")]
    [InlineData("height:120px")]
    [InlineData("vertical-align:super")]
    [InlineData("white-space:normal")]
    [InlineData("text-decoration:line-through")]
    [InlineData("word-spacing:500px")]
    public void UnknownOrChangedGeometryStillRejectsThePage(string style)
    {
        using var document = Convert(Markup(NumericRow("font-family:sans-serif;" + style)));
        document.QuerySelector("p").Should().BeNull();
    }

    [Fact]
    public void MixedFontsDoNotMakeAProseLineJoinable()
    {
        var markup = Markup("")
            .Replace(
                "the lessees Alpha Shipping",
                "<span style='font-family:sans-serif'>the lessees</span> Alpha Shipping"
            );
        using var document = Convert(markup);
        document.QuerySelector("p").Should().BeNull();
    }

    [Fact]
    public void NestedPositionedContentCannotHideBehindAnObstacle()
    {
        var row = NumericRow("font-family:sans-serif")
            .Replace("19,623", "<span style='position:absolute;bottom:148px'>19,623</span>");
        using var document = Convert(Markup(row));
        document.QuerySelector("p").Should().BeNull();
    }

    [Fact]
    public void AwilcoPagePreservesTablesAndReconnectsTheLesseesParagraph()
    {
        using var stream = File.OpenRead(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Esef",
                "awilco-2025-guarantees-page.xhtml.gz"
            )
        );
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        System
            .Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray()))
            .Should()
            .Be("3af8d215634d2504b8042b379b220b06be65729ea1fd2893ace8439dda87ebea");
        var source = XhtmlCompatibility.ExpandEmptyElements(
            System.Text.Encoding.UTF8.GetString(bytes.ToArray())
        );
        using var original = new HtmlParser().ParseDocument(source);
        using var converted = Convert(source);
        converted.Body.TextContent.Should().Be(original.Body.TextContent);
        var borrower = converted
            .QuerySelectorAll("p")
            .Single(p => p.TextContent.StartsWith("The Company has issued guarantees towards"));
        borrower.TextContent.Should().Contain("Awilco LNG 4 AS and Awilco LNG 5 AS respectively");
        borrower.TextContent.Should().Contain("TNOK 43");
        borrower.TextContent.Should().NotContain("19,623");
        foreach (var amount in new[] { "19,623", "27,289", "1,532", "5,324" })
            converted
                .QuerySelectorAll(".t")
                .Should()
                .Contain(line => line.TextContent.Trim() == amount);
        var normalized = new SecDocumentHtmlNormalizer().NormalizeFragment(source);
        var text = new SecDocumentHtmlToMarkdownConverter().Convert(normalized);
        text.Split("\n\n")
            .Should()
            .Contain(block =>
                block.Contains("The Company has issued guarantees towards")
                && block.Contains("Awilco LNG 4 AS and")
                && block.Contains("Awilco LNG 5 AS respectively")
            );
    }
}
