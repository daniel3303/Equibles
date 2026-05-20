using System.Reflection;
using System.Xml.Linq;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the legacy-tolerance arm of InsiderTradingFilingProcessor.ParseOwnershipNature:
/// when the SEC ownership XML omits the ownershipNature element entirely, the method
/// must default to Direct rather than throw — matches the documented "XSD requires
/// it but legacy filings drop it" contract. Tested via reflection (private static).
/// </summary>
public class InsiderTradingFilingProcessorParseOwnershipNatureAbsentTests
{
    private static readonly MethodInfo ParseOwnershipNatureMethod =
        typeof(InsiderTradingFilingProcessor).GetMethod(
            "ParseOwnershipNature",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    [Fact]
    public void ParseOwnershipNature_OwnershipNatureElementAbsent_ReturnsDirect()
    {
        // The XML-doc-style comment above ParseOwnershipNature explicitly enumerates
        // the absent-element case: "I → Indirect, anything else (including 'D' and
        // absent) → Direct. The ownershipNature element is required by the ownership
        // XSD, but legacy filings sometimes omit or misspell it; defaulting to Direct
        // matches the existing inline behavior across ParseTransaction / ParseHolding."
        //
        // The risk: a refactor that "tightens" the helper to enforce the XSD's
        // required-element rule — e.g. throwing on missing ownershipNature, or
        // returning a nullable that the caller then mishandles as Indirect — would
        // compile, pass every existing test (none of which probe this method
        // directly, and none of which feed it a stripped XML), and abort the
        // insider-trading processing pipeline on the first legacy filing that
        // omits the element. The documented tolerance lives ONLY here; nothing
        // upstream short-circuits when the element is missing.
        //
        // Pin the absent-element arm by passing a transaction element with no
        // ownershipNature child at all. Expected: OwnershipNature.Direct.
        var transactionElement = new XElement(
            "nonDerivativeTransaction",
            new XElement("securityTitle", new XElement("value", "Common Stock"))
        );

        var result = (OwnershipNature)ParseOwnershipNatureMethod.Invoke(null, [transactionElement]);

        result.Should().Be(OwnershipNature.Direct);
    }
}
