using System.Reflection;
using System.Xml.Linq;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class NportFilingProcessorParseIssuerCategoryConditionalTests
{
    // Contract: NPORT issuer category comes from the <issuerCat> child element; EDGAR also
    // emits it as an issuerCat ATTRIBUTE on <issuerConditional> when the direct element is
    // absent, so the parser must fall back to that attribute rather than returning null.
    [Fact]
    public void ParseIssuerCategory_DirectElementAbsentConditionalAttributePresent_ReturnsAttribute()
    {
        var element = XElement.Parse(
            "<invstOrSec><issuerConditional issuerCat=\"CORP\" desc=\"Corporate\" /></invstOrSec>"
        );

        var method = typeof(NportFilingProcessor).GetMethod(
            "ParseIssuerCategory",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method.Invoke(null, [element]);

        result.Should().Be("CORP");
    }
}
