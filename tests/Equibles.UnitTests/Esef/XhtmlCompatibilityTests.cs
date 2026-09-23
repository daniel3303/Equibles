using System.Text;
using System.Xml.Linq;
using Equibles.Core.Documents;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Esef;

public class XhtmlCompatibilityTests
{
    private static string Report() =>
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Esef",
                "nostrum-2025-empty-title.xhtml"
            )
        );

    [Fact]
    public void RealReport_RecoversFactsWithoutChangingTheirValuesIdentityOrPeriods()
    {
        var source = Report();
        var parser = new InlineXbrlParser();
        var facts = parser.Parse(source);
        facts.Should().HaveCount(3);
        facts.Should().BeEquivalentTo(parser.Parse(source.Replace("<title/>", "<title></title>")));
        facts.Select(f => f.Value).Should().Equal(274954000m, 372883000m, 3736000m);
        facts
            .Should()
            .OnlyContain(f => f.ConsolidatedLei == "2138007VWEP4MM3J8B29" && f.Unit == "USD");
        facts[0].PeriodEnd.Should().Be(new DateOnly(2025, 12, 31));
        facts[1].PeriodEnd.Should().Be(new DateOnly(2024, 12, 31));
    }

    [Fact]
    public void RealReport_RetrievalContainsTheFiguresAfterAnEmptyTitle()
    {
        var text = Encoding.UTF8.GetString(
            EsefReportContent.Build(
                Report(),
                new SecDocumentHtmlNormalizer(),
                new SecDocumentHtmlToMarkdownConverter()
            )
        );
        text.Should().Contain("274,954").And.Contain("372,883").And.Contain("3,736");
    }

    [Theory]
    [InlineData("title")]
    [InlineData("style")]
    [InlineData("script")]
    [InlineData("textarea")]
    [InlineData("div")]
    public void EmptyNonVoidElements_HaveExplicitEndTags(string tag)
    {
        var source =
            $"<html xmlns=\"http://www.w3.org/1999/xhtml\"><body><{tag}/><p>Following text</p></body></html>";
        var result = XhtmlCompatibility.ExpandEmptyElements(source);
        result.Should().Contain($"<{tag}></{tag}>");
        XDocument
            .Parse(result)
            .Descendants()
            .Select(e => (e.Name, e.Value))
            .Should()
            .Equal(XDocument.Parse(source).Descendants().Select(e => (e.Name, e.Value)));
    }

    [Fact]
    public void VoidElements_KeepTheirOriginalRepresentation()
    {
        const string source =
            "<html xmlns=\"http://www.w3.org/1999/xhtml\"><body><br/><img src=\"image.png\"/></body></html>";
        XhtmlCompatibility.ExpandEmptyElements(source).Should().Be(source);
    }

    [Theory]
    [InlineData("<html><body><title/><p>ordinary HTML</p></body></html>")]
    [InlineData("<DOCUMENT><TYPE>10-K<html><title/><p>SEC envelope</DOCUMENT>")]
    [InlineData("<html xmlns=\"http://www.w3.org/1999/xhtml\"><title/><p>Unclosed</html>")]
    public void NonXmlOrNonXhtml_IsLeftUntouched(string source)
    {
        XhtmlCompatibility.ExpandEmptyElements(source).Should().Be(source);
    }

    [Fact]
    public void ExternalDtd_IsNeverResolved()
    {
        const string source =
            "<!DOCTYPE html SYSTEM 'file:///this-must-not-be-read.dtd'><html xmlns='http://www.w3.org/1999/xhtml'><title/><body>Report</body></html>";
        var result = XhtmlCompatibility.ExpandEmptyElements(source);
        result.Should().Contain("<title></title>").And.Contain("Report");
    }
}
