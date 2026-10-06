using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class InlineXbrlProseConversionStepTests
{
    private const string Start = "Glaston Corporation's financing agreement con-";
    private const string End = "sists of EUR 32 million loans and a EUR 25 million facility.";

    private static string Source(string first = Start, string second = End, string between = "") =>
        "<html xmlns:ix='http://www.xbrl.org/2013/inlineXBRL'><body>"
        + "<ix:nonNumeric name='ifrs-full:Liquidity' contextRef='c1' escape='true' continuedAt='next'>"
        + $"<p>{first}</p></ix:nonNumeric>{between}"
        + $"<ix:continuation id='next'><div><p>{second}</p></div></ix:continuation></body></html>";

    private static IHtmlDocument Convert(string source)
    {
        var document = new HtmlParser(new HtmlParserOptions
        {
            IsAcceptingCustomElementsEverywhere = true,
        }).ParseDocument(source);
        new InlineXbrlProseConversionStep().Execute(document);
        return document;
    }

    [Fact]
    public void AnExplicitContinuationKeepsThePrintedBreakAndEverySourceCharacter()
    {
        var result = Convert(Source());
        var paragraph = result.QuerySelectorAll("p").Should().ContainSingle().Subject;
        paragraph.TextContent.Should().Be(Start + End);
        paragraph.QuerySelectorAll("br").Should().ContainSingle();
        paragraph.InnerHtml.Should().Be(Start + "<br>" + End);
    }

    [Fact]
    public void AChainCanContinueOneParagraphAcrossThreeFragments()
    {
        var source = Source(second: "sists of loans and a")
            .Replace("id='next'", "id='next' continuedAt='last'", StringComparison.Ordinal)
            .Replace("</body>", "<ix:continuation id='last'><p>revolving facility.</p></ix:continuation></body>", StringComparison.Ordinal);
        var result = Convert(source);
        result.QuerySelectorAll("p").Should().ContainSingle();
        result.QuerySelector("p").QuerySelectorAll("br").Should().HaveCount(2);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("cycle")]
    [InlineData("shared")]
    [InlineData("wrong-namespace")]
    [InlineData("unbound-namespace")]
    [InlineData("wrong-target")]
    [InlineData("no-link")]
    [InlineData("no-context")]
    [InlineData("no-name")]
    [InlineData("plain-fact")]
    [InlineData("hidden")]
    [InlineData("excluded")]
    public void UnprovenOrAmbiguousChainsPreserveParagraphBoundaries(string defect)
    {
        var source = Source();
        source = defect switch
        {
            "missing" => source.Replace("id='next'", "id='other'", StringComparison.Ordinal),
            "duplicate" => source.Replace("</body>", "<div id='next'></div></body>", StringComparison.Ordinal),
            "cycle" => source.Replace("id='next'", "id='next' continuedAt='next'", StringComparison.Ordinal),
            "shared" => source.Replace("</body>", "<ix:footnote continuedAt='next'>Another fact.</ix:footnote></body>", StringComparison.Ordinal),
            "wrong-namespace" => source.Replace("http://www.xbrl.org/2013/inlineXBRL", "https://example.com/not-xbrl", StringComparison.Ordinal),
            "unbound-namespace" => source.Replace("xmlns:ix='http://www.xbrl.org/2013/inlineXBRL'", "", StringComparison.Ordinal),
            "wrong-target" => source.Replace("ix:continuation", "div", StringComparison.Ordinal),
            "no-link" => source.Replace("continuedAt='next'", "", StringComparison.Ordinal),
            "no-context" => source.Replace("contextRef='c1'", "", StringComparison.Ordinal),
            "no-name" => source.Replace("name='ifrs-full:Liquidity'", "", StringComparison.Ordinal),
            "plain-fact" => source.Replace("escape='true'", "escape='false'", StringComparison.Ordinal),
            "hidden" => source.Replace("<body>", "<body hidden>", StringComparison.Ordinal),
            "excluded" => source.Replace("<p>" + Start, "<p><ix:exclude>Other text.</ix:exclude>" + Start, StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        Convert(source).QuerySelectorAll("p").Should().HaveCount(2);
    }

    [Theory]
    [InlineData("Finished sentence.", End)]
    [InlineData("A heading:", End)]
    [InlineData(Start, "New paragraph begins here.")]
    [InlineData("The amount is EUR 32", "million.")]
    [InlineData(Start, "25 million.")]
    [InlineData("", End)]
    [InlineData(Start, "")]
    public void CompleteParagraphsHeadingsAndNumericEdgesStaySeparate(string first, string second)
    {
        Convert(Source(first, second)).QuerySelectorAll("p").Should().HaveCount(2);
    }

    [Theory]
    [InlineData("Intervening text.")]
    [InlineData("<div>Other facts.</div>")]
    [InlineData("<table><tr><td>100</td></tr></table>")]
    [InlineData("<h2>Other terms</h2>")]
    [InlineData("<ix:exclude>Excluded text</ix:exclude>")]
    [InlineData("<img alt='Other agreement'>")]
    public void AContinuationDoesNotMoveEvidenceAcrossInterveningContent(string between)
    {
        Convert(Source(between: between)).QuerySelectorAll("p").Should().HaveCount(2);
    }

    [Fact]
    public void ADisconnectedFragmentCannotSearchBackwardsThroughTheDocument()
    {
        var source = Source().Replace("continuedAt='next'", "", StringComparison.Ordinal)
            .Replace("<ix:nonNumeric", "<ix:nonNumeric id='earlier'", StringComparison.Ordinal)
            .Replace("id='next'", "id='next' continuedAt='earlier'", StringComparison.Ordinal);
        Convert(source).QuerySelectorAll("p").Should().HaveCount(2);
    }

    [Fact]
    public void LargeParagraphsRemainSeparate()
    {
        Convert(Source(new string('a', 8000), "continued text.")).QuerySelectorAll("p").Should().HaveCount(2);
        Convert(Source(Start + new string(' ', 8000), End)).QuerySelectorAll("p").Should().HaveCount(2);
        Convert(Source(Start, new string(' ', 8000) + End)).QuerySelectorAll("p").Should().HaveCount(2);
    }

    [Theory]
    [InlineData("display:none")]
    [InlineData("visibility:hidden")]
    [InlineData("opacity:0")]
    [InlineData("content-visibility:hidden")]
    [InlineData("text-decoration:line-through")]
    [InlineData("text-transform:uppercase")]
    [InlineData("clip-path:inset(100%)")]
    [InlineData("display:var(--hidden)")]
    public void InlineOrStylesheetHiddenContentCannotMoveOutOfItsScope(string style)
    {
        foreach (var name in new[] { "p", "div", "ix:continuation" })
        {
            var source = Source().Replace("<" + name, "<" + name + " style='" + style + "'", StringComparison.Ordinal);
            Convert(source).QuerySelectorAll("p").Should().HaveCount(2);
            source = Source().Replace("<body>", $"<head><style>.hidden {{{style}}}</style></head><body>", StringComparison.Ordinal)
                .Replace("<" + name, "<" + name + " class='hidden'", StringComparison.Ordinal);
            Convert(source).QuerySelectorAll("p").Should().HaveCount(2);
        }
    }

    [Theory]
    [InlineData("<style>@media screen {.hidden{display:none}}</style>")]
    [InlineData("<style>@import url(https://example.com/style.css);</style>")]
    [InlineData("<style>@supports (display:grid) {.hidden{display:none}}</style>")]
    [InlineData("<link rel='stylesheet' href='https://example.com/style.css'>")]
    [InlineData("<style media='(max-width:500px)'>p{display:none}</style>")]
    [InlineData("<style scoped>p{display:none}</style>")]
    [InlineData("<style type='application/unknown'>p{display:none}</style>")]
    public void UnknownOrConditionalStylesPreserveBoundaries(string head)
    {
        Convert(Source().Replace("<body>", "<head>" + head + "</head><body>", StringComparison.Ordinal))
            .QuerySelectorAll("p").Should().HaveCount(2);
    }

    [Theory]
    [InlineData("blockquote")]
    [InlineData("ul")]
    [InlineData("li")]
    [InlineData("pre")]
    [InlineData("del")]
    [InlineData("q")]
    public void ProseDoesNotCrossSemanticContainers(string container)
    {
        var source = Source().Replace("<ix:nonNumeric", "<" + container + "><ix:nonNumeric", StringComparison.Ordinal)
            .Replace("</ix:nonNumeric>", "</ix:nonNumeric></" + container + ">", StringComparison.Ordinal);
        Convert(source).QuerySelectorAll("p").Should().HaveCount(2);
    }

    [Theory]
    [InlineData("ix:nonNumeric")]
    [InlineData("ix:continuation")]
    public void NestedFactOwnersCannotExchangeParagraphs(string parent)
    {
        var source = Source().Replace("<ix:continuation", $"<{parent} name='ifrs-full:OtherFact' contextRef='other'><ix:continuation", StringComparison.Ordinal)
            .Replace("</body>", $"</{parent}></body>", StringComparison.Ordinal);
        Convert(source).QuerySelectorAll("p").Should().HaveCount(2);
    }

    [Fact]
    public void ASharedOuterFactCanRetainItsOwnNestedDisclosureParagraph()
    {
        var source = Source().Replace("<body>", "<body><ix:continuation id='outer'>", StringComparison.Ordinal)
            .Replace("</body>", "</ix:continuation></body>", StringComparison.Ordinal);
        Convert(source).QuerySelectorAll("p").Should().ContainSingle();
    }

    [Fact]
    public void AContinuationInAnotherOuterFactKeepsThatBoundary()
    {
        var source = Source(second: "sists of loans and a")
            .Replace("<body>", "<body><ix:continuation id='outer'>", StringComparison.Ordinal)
            .Replace("id='next'", "id='next' continuedAt='last'", StringComparison.Ordinal)
            .Replace("</body>", "</ix:continuation><ix:continuation id='other'><ix:continuation id='last'><p>revolving facility.</p></ix:continuation></ix:continuation></body>", StringComparison.Ordinal);
        var result = Convert(source);
        result.QuerySelectorAll("p").Should().HaveCount(2);
        result.QuerySelectorAll("p")[0].TextContent.Should().Be(Start + "sists of loans and a");
        result.QuerySelectorAll("p")[1].TextContent.Should().Be("revolving facility.");
    }

    [Fact]
    public void AHiddenDescendantCannotBecomeVisibleInTheJoinedParagraph()
    {
        Convert(Source(second: "sists of <span hidden>other facilities</span> EUR 32 million."))
            .QuerySelectorAll("p").Should().HaveCount(2);
    }

    [Fact]
    public void UnrelatedHiddenSelectorsDoNotBlockVisibleProse()
    {
        var source = Source().Replace("<body>", "<head><style>.other{display:none} p{color:black}</style></head><body>", StringComparison.Ordinal);
        Convert(source).QuerySelectorAll("p").Should().ContainSingle();
    }

    [Theory]
    [InlineData(".hidden{display:none}.hidden{display:block}")]
    [InlineData(".\\68 idden{display:none}")]
    public void OverridesAndEscapedSelectorsDoNotMakeHiddenContentSafe(string css)
    {
        var source = Source().Replace("<body>", $"<head><style>{css}</style></head><body>", StringComparison.Ordinal)
            .Replace("<p>", "<p class='hidden'>", StringComparison.Ordinal);
        Convert(source).QuerySelectorAll("p").Should().HaveCount(2);
    }

    [Fact]
    public void RetainedLiquidityDisclosureRejoinsOnlyItsUnfinishedParagraph()
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Esef",
            "glaston-2025-liquidity-continuation.xhtml.gz"));
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var source = reader.ReadToEnd();
        System.Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)))
            .Should().Be("77ab8283722c187202f68144ee50cef099fe09a5d474a77408258b41aa88c4d2");
        var normalized = new SecDocumentHtmlNormalizer().NormalizeFragment(source);
        var result = new HtmlParser().ParseDocument(normalized);
        var paragraph = result.QuerySelectorAll("p")
            .Single(p => p.TextContent.StartsWith("Glaston Corporation has agreed", StringComparison.Ordinal));
        paragraph.TextContent.Should().Contain("EUR 32 million").And.Contain("EUR 25 million")
            .And.Contain("Revolving Credit Facility").And.NotContain("Committed credit facilities");
        var raw = new HtmlParser(new HtmlParserOptions { IsAcceptingCustomElementsEverywhere = true }).ParseDocument(source);
        var before = Regex.Matches(raw.Body.TextContent, @"\d+(?:[.,]\d+)*").Select(m => m.Value);
        var after = Regex.Matches(Convert(source).Body.TextContent, @"\d+(?:[.,]\d+)*").Select(m => m.Value);
        after.Should().Equal(before);
        new SecDocumentHtmlToMarkdownConverter().Convert(normalized)
            .Should().Contain("financing agreement con-")
            .And.Contain("sists of EUR 32 million");
    }
}
