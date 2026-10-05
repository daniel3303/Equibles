using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.HostedService.Services;
using FluentAssertions;

namespace Equibles.UnitTests.Esef;

public class EsefRetrievalStylesTests
{
    private const string Namespace = "http://www.w3.org/1999/xhtml";

    [Theory]
    [InlineData(
        "teixeira-duarte-2024",
        28_900_183,
        1_134_006,
        "6F653A4287FA07D953873BC045EB337E426C7A382E5D73BFF6EEF8443D77A3D9",
        "| Financiamentos obtidos | 14.2 e 21 | 170.332 | 191.788 |"
    )]
    [InlineData(
        "teixeira-duarte-2025",
        33_501_816,
        1_278_650,
        "2F5A2F4D5EAB330FD42B038982F2A9ED0D4D2CDE891A4166D709801A4DF418D4",
        "| Financiamentos obtidos | 14.2 e 21 | 227.580 | 170.332 |"
    )]
    [InlineData(
        "jd-sports-2026",
        20_768_126,
        929_052,
        "2E9284D51B74FA650113D1A5BA215D8E2C0FC3CE9FD624D59484D31A21C6FE9D",
        "21. Interest-Bearing Loans and Borrowings"
    )]
    [InlineData(
        "bff-2025",
        34_672_821,
        2_048_711,
        "C599CD1356A8F79910CFCA43F03DD28D085BF77BD144421D17C36ABF703BA474",
        "Debiti verso banche"
    )]
    public void Build_RecordedOversizedReport_RetainsTheCompleteNormalizedText(
        string fixture,
        int characters,
        int expectedBytes,
        string expectedHash,
        string expectedBorrowingText
    )
    {
        using var file = File.OpenRead(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Esef",
                $"{fixture}-envelope.xhtml.gz"
            )
        );
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var source = reader.ReadToEnd();
        source.Length.Should().Be(characters);

        var compact = EsefReportContent.PrepareRetrievalMarkup(source);
        compact.Length.Should().BeLessThan(EsefReportContent.MaxRetrievalHtmlChars);
        EsefReportContent.ExceedsRetrievalLimit(source).Should().BeFalse();
        var bytes = EsefReportContent.Build(
            source,
            new SecDocumentHtmlNormalizer(),
            new SecDocumentHtmlToMarkdownConverter()
        );

        bytes.Length.Should().Be(expectedBytes);
        Convert.ToHexString(SHA256.HashData(bytes)).Should().Be(expectedHash);
        Encoding.UTF8.GetString(bytes).Should().Contain(expectedBorrowingText);
    }

    [Fact]
    public void Prepare_WithinTheLimit_DoesNotRewriteTheMarkup()
    {
        var source =
            $"<html xmlns='{Namespace}'><body><span style='left:1px'>Debt 123.45</span></body></html>";
        EsefReportContent.PrepareRetrievalMarkup(source).Should().Be(source);
    }

    [Fact]
    public void Prepare_LayoutMetadataPreservesMarkdownStructureAndSemanticClasses()
    {
        const string body = """
            <div class="layout-one layout-two" id="section"><span class="layout-three layout-four" id="span-id">Debt 123</span></div>
            <span id="standalone">Interest 456</span><a id="anchor" class="link-style" href="#section">Source</a>
            <span class="math display" id="equation">x + y</span>
            <div class="line-block">Line one<br/>Line two</div>
            <div class="footnotes" role="doc-endnotes"><p>Footnote</p></div>
            <div class="item-list-element-wrapper"><span>1.</span><div style="display:inline">List item</div></div>
            <div class="highlight-source-csharp"><pre><code>var debt = 123;</code></pre></div>
            <div class="language-json"><pre><code>{}</code></pre></div>
            <div class="lang-text"><pre><code>Debt</code></pre></div>
            <div class="brush: json"><pre><code>{"debt":123}</code></pre></div>
            <code class="language-js">value</code>
            <span xmlns:custom="urn:custom" custom:class="keep-class" custom:id="keep-id">Namespaced</span>
            <svg xmlns="http://www.w3.org/2000/svg"><span class="keep-svg" id="keep-svg-id">Diagram</span></svg>
            """;
        var source = LargeReport(body);
        var compact = EsefReportContent.PrepareRetrievalMarkup(source);
        var document = XDocument.Parse(compact);
        var layoutSpan = document.Descendants(XName.Get("span", Namespace)).First();
        layoutSpan.Attribute("class").Value.Should().NotBeNullOrWhiteSpace();
        layoutSpan
            .Attribute("class")
            .Value.Length.Should()
            .BeLessThan("layout-three layout-four".Length);
        layoutSpan.Attribute("id").Value.Should().BeEmpty();
        compact.Should().Contain("id=\"section\"").And.Contain("id=\"standalone\"");
        compact.Should().Contain("id=\"anchor\" class=\"link-style\" href=\"#section\"");
        foreach (
            var meaningfulClass in new[]
            {
                "math display",
                "line-block",
                "footnotes",
                "item-list-element-wrapper",
                "highlight-source-csharp",
                "language-json",
                "lang-text",
                "brush: json",
                "language-js",
                "keep-svg",
            }
        )
        {
            compact.Should().Contain($"class=\"{meaningfulClass}\"");
        }
        compact
            .Should()
            .Contain("custom:class=\"keep-class\"")
            .And.Contain("custom:id=\"keep-id\"");

        // Removing unused font-size padding produces the same document without crossing the cap.
        var small = source.Replace(
            new string('0', EsefReportContent.MaxRetrievalHtmlChars),
            "0",
            StringComparison.Ordinal
        );
        var normalizer = new SecDocumentHtmlNormalizer();
        var converter = new SecDocumentHtmlToMarkdownConverter();
        EsefReportContent
            .Build(source, normalizer, converter)
            .Should()
            .Equal(EsefReportContent.Build(small, normalizer, converter));
    }

    [Fact]
    public void Prepare_OversizedMarkup_PreservesTextAttributesAndNonLayoutStyles()
    {
        var source = LargeReport(
            """
            <p style="font-size:9px;font-weight:bold;font-style:italic;text-align:center;display:none;color:red;text-decoration:line-through" data-value="Debt &amp; interest 1&#x9;234&#xA;56&#xD;78">Debt &lt; 123.45&#xD;Interest 67.89</p>
            <svg xmlns="http://www.w3.org/2000/svg" style="font-size:9px;left:2px"><text>Diagram 456</text></svg>
            <!-- Complete comment --><p><![CDATA[Exact <CDATA> & 789]]></p><?report retain?>
            <table><tr><td rowspan="2">Liabilities</td><td>1,234.56</td></tr><tr><td>987.65</td></tr></table>
            """
        );
        var compact = EsefReportContent.PrepareRetrievalMarkup(source);
        var before = XDocument.Parse(source, LoadOptions.PreserveWhitespace);
        var after = XDocument.Parse(compact, LoadOptions.PreserveWhitespace);

        after.Root.Value.Should().Be(before.Root.Value);
        after
            .Descendants()
            .Select(element => element.Name)
            .Should()
            .Equal(before.Descendants().Select(element => element.Name));
        after
            .Descendants()
            .Attributes()
            .Where(attribute => attribute.Name != "style")
            .Select(attribute => (attribute.Name, attribute.Value))
            .Should()
            .Equal(
                before
                    .Descendants()
                    .Attributes()
                    .Where(attribute => attribute.Name != "style")
                    .Select(attribute => (attribute.Name, attribute.Value))
            );
        compact
            .Should()
            .Contain(
                "font-weight:bold;font-style:italic;text-align:center;display:none;color:red;text-decoration:line-through"
            );
        compact.Should().Contain("style=\"font-size:9px;left:2px\"");
        compact.Should().Contain("<![CDATA[Exact <CDATA> & 789]]>");
        compact.Should().Contain("<!-- Complete comment -->").And.Contain("<?report retain?>");
    }

    [Theory]
    [InlineData("rgba(0, 0, 0, 0)")]
    [InlineData("RGB(10, 20, 30)")]
    [InlineData("rgba(100%, 0%, 0%, .5)")]
    public void Prepare_NumericColorFunctions_RemoveOnlyUnusedLayout(string color)
    {
        var source = LargeReport(
            $"<p style=\"font-size:12px;-webkit-text-stroke:0.18px {color};color:{color};display:none;text-decoration:line-through\">Text 42</p>"
        );
        var compact = XDocument.Parse(EsefReportContent.PrepareRetrievalMarkup(source));
        compact
            .Descendants(XName.Get("p", Namespace))
            .Single(element => element.Attribute("style") != null)
            .Attribute("style")
            .Value.Should()
            .Be($"color:{color};display:none;text-decoration:line-through");
    }

    [Theory]
    [InlineData("font-family:'font;name';font-size:9px")]
    [InlineData("left:calc(1px + 2px);font-size:9px")]
    [InlineData("/* comment */font-size:9px")]
    [InlineData("-webkit-text-stroke:rgba(0,0,0,0;display:none);font-size:9px")]
    [InlineData("-webkit-text-stroke:rgba(0,0,0,0;font-size:9px")]
    [InlineData("-webkit-text-stroke:rgba(var(--red),0,0,0);font-size:9px")]
    [InlineData("-webkit-text-stroke:rgba(0,0,0,0);left:calc(1px + 2px)")]
    [InlineData("--custom:var(--size);font-size:9px")]
    [InlineData("font-family:font\\name;font-size:9px")]
    public void Prepare_ComplexCss_KeepsTheWholeAttribute(string style)
    {
        var source = LargeReport($"<p style=\"{style}\">Text 42</p>");
        var compact = XDocument.Parse(EsefReportContent.PrepareRetrievalMarkup(source));
        compact
            .Descendants(XName.Get("p", Namespace))
            .Single(element => element.Attribute("style") != null)
            .Attribute("style")
            .Value.Should()
            .Be(style);
    }

    [Theory]
    [InlineData("<broken>")]
    [InlineData("&undeclared;")]
    public void Prepare_InvalidXml_KeepsTheOriginalSizeRefusal(string invalid)
    {
        var source = LargeReport("<p>Prefix 123</p>" + invalid);
        EsefReportContent.PrepareRetrievalMarkup(source).Should().Be(source);
        EsefReportContent.ExceedsRetrievalLimit(source).Should().BeTrue();
    }

    [Fact]
    public void Prepare_OrdinaryHtmlWithoutTheXhtmlNamespace_KeepsTheExistingLimit()
    {
        var source = LargeReport("<p>Complete text</p>").Replace($" xmlns=\"{Namespace}\"", "");
        EsefReportContent.PrepareRetrievalMarkup(source).Should().Be(source);
    }

    [Fact]
    public void Build_ActualTextStillExceedsTheLimit_NeverReturnsAPrefix()
    {
        var source = LargeReport(
            "<p>" + new string('a', EsefReportContent.MaxRetrievalHtmlChars) + "</p><p>End 999</p>"
        );
        EsefReportContent.ExceedsRetrievalLimit(source).Should().BeTrue();
        EsefReportContent
            .Build(
                source,
                new SecDocumentHtmlNormalizer(),
                new SecDocumentHtmlToMarkdownConverter()
            )
            .Should()
            .BeEmpty();
    }

    private static string LargeReport(string body) =>
        $"<html xmlns=\"{Namespace}\"><body><div style=\"font-size:{new string('0', EsefReportContent.MaxRetrievalHtmlChars)}px\">Start 101</div>{body}<p id=\"last\">End 202</p></body></html>";
}
