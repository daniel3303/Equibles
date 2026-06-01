using System.Xml.Linq;
using Equibles.Sec.HostedService.Helpers;

namespace Equibles.UnitTests.Sec;

public class EdgarXmlSubmissionParserAttrTests
{
    [Fact]
    public void Attr_WhitespaceOnlyValue_ReturnsNull()
    {
        // Contract (from the doc-comment): "Trimmed value of the named attribute, or null when
        // missing or empty." A whitespace-only value trims to empty, so the result must be null —
        // matching the sibling Val helper, which callers chain through `?? default`. Oracle derived
        // from the contract, not the body.
        var element = new XElement("issuerStateCountry", new XAttribute("issuerCountry", "   "));

        var result = EdgarXmlSubmissionParser.Attr(element, "issuerCountry");

        result.Should().BeNull();
    }
}
