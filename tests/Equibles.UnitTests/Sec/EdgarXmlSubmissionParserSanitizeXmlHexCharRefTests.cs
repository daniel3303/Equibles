using System.Xml.Linq;
using Equibles.Sec.HostedService.Helpers;

namespace Equibles.UnitTests.Sec;

public class EdgarXmlSubmissionParserSanitizeXmlHexCharRefTests
{
    [Fact]
    public void SanitizeXml_HexadecimalCharacterReference_IsPreservedAndDecodes()
    {
        // Contract: escape only STRAY ampersands — never the ampersand that opens a valid
        // character reference. Oracle from the XML spec, not the body: a hex char ref
        // &#xA9; denotes U+00A9 '©'. The escaping regex's "#x[\da-fA-F]+;" branch is the one
        // the existing decimal (&#38;) and named-entity tests don't reach; double-escaping it
        // would corrupt the ref into the literal text "&#xA9;" instead of decoding to '©'.
        var submission =
            "<XML>\n"
            + "<edgarSubmission><issuerName>Acme &#xA9; 2024</issuerName></edgarSubmission>\n"
            + "</XML>";

        var sanitized = EdgarXmlSubmissionParser.SanitizeXml(submission);

        var issuerName = XDocument
            .Parse(sanitized)
            .Root.Elements()
            .First(e => e.Name.LocalName == "issuerName")
            .Value;
        issuerName.Should().Be("Acme © 2024");
    }
}
