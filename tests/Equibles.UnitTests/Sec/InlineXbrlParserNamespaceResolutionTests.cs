using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The inline parser resolves each fact's concept prefix to the namespace URI
/// the document declares for it, so downstream classification can tell a
/// filer-extension concept (company-owned namespace) from a reference
/// taxonomy. Prefix lookup is case-insensitive because the HTML parser
/// lowercases attribute names while fact names keep the author's casing; an
/// undeclared prefix yields a null namespace, never a guess.
/// </summary>
public class InlineXbrlParserNamespaceResolutionTests
{
    private const string DocOpen =
        "<html "
        + "xmlns=\"http://www.w3.org/1999/xhtml\" "
        + "xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\" "
        + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\" "
        + "xmlns:ADBE=\"http://www.adobe.com/20231201\" "
        + "><body><div style=\"display:none\"><ix:header><ix:resources>"
        + "<xbrli:context id=\"C1\">"
        + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
        + "  <xbrli:period><xbrli:instant>2024-06-30</xbrli:instant></xbrli:period>"
        + "</xbrli:context>"
        + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
        + "</ix:resources></ix:header></div>";

    private const string DocClose = "</body></html>";

    [Fact]
    public void Parse_DeclaredPrefixes_ResolveToTheirNamespaceUris()
    {
        var html =
            DocOpen
            + "<p><ix:nonFraction name=\"us-gaap:Revenues\" contextRef=\"C1\" unitRef=\"u\">100</ix:nonFraction></p>"
            + "<p><ix:nonFraction name=\"ADBE:Subscribers\" contextRef=\"C1\" unitRef=\"u\">7</ix:nonFraction></p>"
            + DocClose;

        var facts = new InlineXbrlParser().Parse(html);

        facts.Should().HaveCount(2);
        facts[0].Namespace.Should().Be("http://fasb.org/us-gaap/2018-01-31");
        facts[1].Namespace.Should().Be("http://www.adobe.com/20231201");
    }

    [Fact]
    public void Parse_UndeclaredPrefix_YieldsNullNamespace()
    {
        var html =
            DocOpen
            + "<p><ix:nonFraction name=\"mystery:Metric\" contextRef=\"C1\" unitRef=\"u\">5</ix:nonFraction></p>"
            + DocClose;

        var facts = new InlineXbrlParser().Parse(html);

        facts.Should().ContainSingle();
        facts[0].Namespace.Should().BeNull();
    }
}
