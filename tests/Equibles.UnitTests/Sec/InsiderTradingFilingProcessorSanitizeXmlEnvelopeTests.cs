using System.Xml.Linq;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorSanitizeXmlEnvelopeTests
{
    // Contract (doc-comment): "SEC filings wrap the actual XML inside an SGML
    // envelope. Extract the XML content from within <XML>...</XML> tags."
    // The existing SanitizeXml test feeds an enveloped input but only asserts
    // ampersand behaviour — it never checks the envelope is actually stripped.
    // If the extraction slice regressed, XDocument.Parse would see <XML> as the
    // root and silently mis-structure every Form 3/4 filing. Use a lowercase
    // <xml> to also pin the documented OrdinalIgnoreCase match.
    [Fact]
    public void SanitizeXml_StripsSgmlEnvelope_SoOwnershipDocumentIsTheParsedRoot()
    {
        var input =
            "<SEC-DOCUMENT>header noise<xml>\n"
            + "  <ownershipDocument><issuer>ACME</issuer></ownershipDocument>\n"
            + "</xml>trailing noise</SEC-DOCUMENT>";

        var result = InsiderTradingFilingProcessor.SanitizeXml(input);

        result.Should().NotContainAny("<xml>", "</xml>", "SEC-DOCUMENT", "noise");
        result.Should().StartWith("<ownershipDocument>");
        XDocument.Parse(result).Root!.Name.LocalName.Should().Be("ownershipDocument");
    }
}
