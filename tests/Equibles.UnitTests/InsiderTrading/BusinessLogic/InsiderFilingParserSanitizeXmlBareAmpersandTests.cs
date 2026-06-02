using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingParserSanitizeXmlBareAmpersandTests
{
    // Contract (doc-comment): "Fix unescaped ampersands in entity names." A bare
    // "&" in a company name (AT&T, Procter & Gamble) makes the filing unparseable,
    // so the sanitizer must escape it to "&amp;" — while leaving an already-escaped
    // "&amp;" untouched (no double-encoding into "&amp;amp;"). The existing
    // SanitizeXml tests only pin numeric-character-reference preservation; the
    // headline case — a bare ampersand sitting beside an already-escaped one in the
    // same value — is unpinned. A naïve `Replace("&", "&amp;")` would pass every
    // numeric-entity test yet corrupt this one, so it is the discriminating case.
    [Fact]
    public void SanitizeXml_BareAmpersandBesideEscapedEntity_EscapesBareKeepsEscaped()
    {
        var input =
            "<XML><ownershipDocument><issuer><issuerName>AT&T &amp; Sons</issuerName></issuer></ownershipDocument></XML>";

        var result = InsiderFilingParser.SanitizeXml(input);

        // The bare ampersand is now escaped and the document parses; the
        // already-escaped one was not double-encoded, so the decoded text round-trips.
        XDocument.Parse(result).Root!.Value.Should().Be("AT&T & Sons");
        result.Should().NotContain("&amp;amp;");
    }
}
