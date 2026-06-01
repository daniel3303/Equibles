using System.Xml.Linq;
using Equibles.Sec.HostedService.Helpers;

namespace Equibles.UnitTests.Sec;

public class EdgarXmlSubmissionParserSanitizeXmlTests
{
    [Fact]
    public void SanitizeXml_StrayAmpersandAmongValidEntities_EscapesOnlyStrayAndParsesAsXml()
    {
        // Contract: strip the <XML> envelope and escape STRAY ampersands so the payload parses
        // as XML — without double-escaping the five XML predefined entities or numeric character
        // references. Oracle derived from the XML spec, not the implementation: &amp; and &#38;
        // both decode to '&', and the bare '&' in "AT&T" must survive as a literal '&'.
        var submission =
            "<SEC-HEADER>preamble</SEC-HEADER>\n"
            + "<XML>\n"
            + "<edgarSubmission><issuerName>Smith &amp; Co AT&T Tom &#38; Jerry</issuerName></edgarSubmission>\n"
            + "</XML>\n"
            + "<TRAILER>end</TRAILER>";

        var sanitized = EdgarXmlSubmissionParser.SanitizeXml(submission);

        var issuerName = XDocument
            .Parse(sanitized)
            .Root.Elements()
            .First(e => e.Name.LocalName == "issuerName")
            .Value;
        issuerName.Should().Be("Smith & Co AT&T Tom & Jerry");
    }
}
