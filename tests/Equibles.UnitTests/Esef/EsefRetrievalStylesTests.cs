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

    [Fact]
    public void Build_RecordedOversizedReport_RetainsTheCompleteNormalizedText()
    {
        using var file = File.OpenRead(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Esef",
                "teixeira-duarte-2024-envelope.xhtml.gz"
            )
        );
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var source = reader.ReadToEnd();
        source.Length.Should().Be(28_900_183);

        var compact = EsefReportContent.PrepareRetrievalMarkup(source);
        compact.Length.Should().BeLessThan(EsefReportContent.MaxRetrievalHtmlChars);
        EsefReportContent.ExceedsRetrievalLimit(source).Should().BeFalse();
        var bytes = EsefReportContent.Build(
            source,
            new SecDocumentHtmlNormalizer(),
            new SecDocumentHtmlToMarkdownConverter()
        );

        bytes.Length.Should().Be(1_134_006);
        Convert
            .ToHexString(SHA256.HashData(bytes))
            .Should()
            .Be("6F653A4287FA07D953873BC045EB337E426C7A382E5D73BFF6EEF8443D77A3D9");
        Encoding
            .UTF8.GetString(bytes)
            .Should()
            .Contain("| Financiamentos obtidos | 14.2 e 21 | 170.332 | 191.788 |");
    }

    [Fact]
    public void Prepare_WithinTheLimit_DoesNotRewriteTheMarkup()
    {
        var source =
            $"<html xmlns='{Namespace}'><body><span style='left:1px'>Debt 123.45</span></body></html>";
        EsefReportContent.PrepareRetrievalMarkup(source).Should().Be(source);
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
    [InlineData("font-family:'font;name';font-size:9px")]
    [InlineData("left:calc(1px + 2px);font-size:9px")]
    [InlineData("/* comment */font-size:9px")]
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
