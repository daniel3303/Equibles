using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.HostedService.Services;
using FluentAssertions;

namespace Equibles.UnitTests.Esef;

public class EsefViewerScriptTests
{
    private const string Xhtml = "http://www.w3.org/1999/xhtml";

    [Theory]
    [InlineData("application/x.ixbrl-viewer+json")]
    [InlineData("application/json")]
    [InlineData("text/javascript")]
    public void Build_SmallReport_AlreadyOmitsScriptContent(string type)
    {
        var html = Report(
            $"<p>Loans 123</p><script type=\"{type}\">Hidden 987</script><p>Terms 456</p>"
        );
        EsefReportContent.PrepareRetrievalMarkup(html).Should().Be(html);
        Build(html)
            .Should()
            .Contain("Loans 123")
            .And.Contain("Terms 456")
            .And.NotContain("Hidden 987");
    }

    [Theory]
    [InlineData("<script />")]
    [InlineData("<script></script>")]
    [InlineData("<script><![CDATA[if (x < 2) alert('Hidden 987');]]></script>")]
    [InlineData("<script>Hidden &lt;987&gt;</script>")]
    public void Prepare_OversizedReport_KeepsSiblingsAfterEveryScriptShape(string script)
    {
        var source = LargeReport(
            $"<p>Before 123</p>{script}<p>After 456</p><script>Hidden 987</script><p>Last 789</p>"
        );
        var prepared = EsefReportContent.PrepareRetrievalMarkup(source);
        var document = XDocument.Parse(prepared);
        document.Descendants(XName.Get("script", Xhtml)).Should().BeEmpty();
        document
            .Descendants(XName.Get("p", Xhtml))
            .Select(e => e.Value)
            .Should()
            .Equal("Before 123", "After 456", "Last 789");
        Build(source)
            .Should()
            .Contain("Before 123")
            .And.Contain("After 456")
            .And.Contain("Last 789")
            .And.NotContain("Hidden 987");
    }

    [Fact]
    public void Prepare_ScriptContainingXml_PreservesTheSizeRefusal()
    {
        var source = LargeReport(
            "<script><value xmlns=\"urn:financial\">123</value></script><p>After 456</p>"
        );
        EsefReportContent.PrepareRetrievalMarkup(source).Should().Be(source);
        EsefReportContent.ExceedsRetrievalLimit(source).Should().BeTrue();
        Build(source).Should().BeEmpty();
    }

    [Fact]
    public void Prepare_MalformedScript_PreservesTheOriginalSizeRefusal()
    {
        var source = LargeReport("<script>Hidden 987<p>After 456</p>");
        EsefReportContent.PrepareRetrievalMarkup(source).Should().Be(source);
        Build(source).Should().BeEmpty();
    }

    [Fact]
    public void Prepare_EscapedScriptSpellingInProse_PreservesIt()
    {
        var source = LargeReport("<p>&lt;script&gt;Debt 123&lt;/script&gt;</p>");
        XDocument
            .Parse(EsefReportContent.PrepareRetrievalMarkup(source))
            .Descendants(XName.Get("p", Xhtml))
            .Single()
            .Value.Should()
            .Be("<script>Debt 123</script>");
    }

    [Fact]
    public void Prepare_ForeignScriptElement_PreservesItsContents()
    {
        var source = LargeReport(
            "<script xmlns=\"urn:financial\">Preserve 123</script><p>After 456</p>"
        );
        XDocument
            .Parse(EsefReportContent.PrepareRetrievalMarkup(source))
            .Descendants(XName.Get("script", "urn:financial"))
            .Single()
            .Value.Should()
            .Be("Preserve 123");
    }

    [Fact]
    public void Prepare_BigReadableBodyAfterScript_StillRefusesRatherThanTruncating()
    {
        var source = Report(
            "<script>Hidden 987</script><p>"
                + new string('a', EsefReportContent.MaxRetrievalHtmlChars)
                + "</p><p>End 456</p>"
        );
        EsefReportContent.PrepareRetrievalMarkup(source).Should().Be(source);
        Build(source).Should().BeEmpty();
    }

    [Fact]
    public void Build_RealViewerEnvelope_RetainsEveryTaggedFactAndTheReportBody()
    {
        using var file = File.OpenRead(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Esef",
                "ferragamo-2025-viewer-envelope.xhtml.gz"
            )
        );
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var source = reader.ReadToEnd();
        source.Length.Should().BeGreaterThan(EsefReportContent.MaxRetrievalHtmlChars);
        var prepared = EsefReportContent.PrepareRetrievalMarkup(source);
        prepared.Length.Should().BeLessThan(EsefReportContent.MaxRetrievalHtmlChars);
        var parser = new InlineXbrlParser();
        parser.Parse(prepared).Should().BeEquivalentTo(parser.Parse(source));
        var text = Build(source);
        text.Should().Contain("Salvatore Ferragamo").And.NotContain("sourceReports");
        text.Length.Should().BeGreaterThan(1_000_000);
        text.Should()
            .Contain("Prestiti e finanziamenti 22 104.301 113.291")
            .And.Contain("covenant finanziario");
    }

    private static string Report(string body) =>
        $"<html xmlns=\"{Xhtml}\"><body>{body}</body></html>";

    private static string LargeReport(string body) =>
        Report(
            $"<div style=\"font-size:{new string('0', EsefReportContent.MaxRetrievalHtmlChars)}px\" />{body}"
        );

    private static string Build(string source) =>
        Encoding.UTF8.GetString(
            EsefReportContent.Build(
                source,
                new SecDocumentHtmlNormalizer(),
                new SecDocumentHtmlToMarkdownConverter()
            )
        );
}
