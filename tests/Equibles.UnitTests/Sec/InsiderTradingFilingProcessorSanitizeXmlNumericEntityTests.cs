using System.Xml.Linq;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorSanitizeXmlNumericEntityTests
{
    // Contract: SanitizeXml escapes bare ampersands so the result is well-formed
    // XML, but must NOT double-escape already-valid references. SEC filings carry
    // accented insider names as hex numeric character references (e.g. é = &#xE9;).
    // Double-escaping to &amp;#xE9; would corrupt every such name. Pin that a valid
    // hex char-ref survives intact and the output still parses.
    [Fact]
    public void SanitizeXml_HexNumericCharacterReference_IsPreservedNotDoubleEscaped()
    {
        var input = "<XML><ownershipDocument><name>Jos&#xE9;</name></ownershipDocument></XML>";

        var result = InsiderTradingFilingProcessor.SanitizeXml(input);

        result.Should().Contain("&#xE9;").And.NotContain("&amp;#xE9;");
        XDocument.Parse(result).Root!.Value.Should().Be("José");
    }
}
