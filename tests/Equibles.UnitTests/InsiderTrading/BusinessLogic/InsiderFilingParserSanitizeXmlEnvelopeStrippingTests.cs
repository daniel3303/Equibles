using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingParserSanitizeXmlEnvelopeStrippingTests
{
    // Contract (doc-comment): "SEC filings wrap the actual XML inside an SGML
    // envelope." A real submission carries SGML headers BEFORE <XML> and trailer
    // markup AFTER </XML>; SanitizeXml must return only the inner ownership XML.
    // Existing SanitizeXml/TryGetOwnershipRoot tests place <XML> at the exact
    // string boundaries, so the slice with non-zero start and surrounding junk —
    // the case the extraction actually exists for — is unpinned.
    [Fact]
    public void SanitizeXml_PayloadWrappedInSgmlHeaderAndTrailer_ReturnsOnlyInnerXml()
    {
        var enveloped =
            "<SEC-HEADER>ACME & CO</SEC-HEADER>"
            + "<XML><ownershipDocument><issuer /></ownershipDocument></XML>"
            + "<DOCUMENT-TRAILER>ignored</DOCUMENT-TRAILER>";

        var result = InsiderFilingParser.SanitizeXml(enveloped);

        result.Should().Be("<ownershipDocument><issuer /></ownershipDocument>");
        // The stripped SGML wrapper (and its bare ampersand) is gone, so the
        // remaining payload is well-formed XML.
        XDocument.Parse(result).Root!.Name.LocalName.Should().Be("ownershipDocument");
    }
}
