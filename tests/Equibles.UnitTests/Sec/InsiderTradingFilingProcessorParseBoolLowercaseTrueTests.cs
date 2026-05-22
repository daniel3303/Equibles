using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Sibling to InsiderTradingFilingProcessorTests' ParseBool digit-one and
/// digit-zero pins. Those cover the "1"/"0" SEC-XML representation. The
/// `value is "1" or "true" or "True" or "TRUE"` predicate also accepts the
/// textual xsd:boolean form — "true" (lowercase, the canonical XML Schema
/// canonical-lexical form). A refactor that trims the predicate back to just
/// the digit arms (intuitive to anyone reading "isDirector" as a numeric
/// flag) would compile, pass the existing digit pins, and silently drop the
/// every-textual-true encoding emitted by older filers and EDGAR's
/// pre-XBRL ownership wire format.
/// </summary>
public class InsiderTradingFilingProcessorParseBoolLowercaseTrueTests
{
    [Fact]
    public void ParseBool_LowercaseTrue_ReturnsTrue()
    {
        // The lowercase "true" arm is the canonical xsd:boolean form and the
        // representation older Form 4 filings emit. Pin it explicitly so the
        // arm survives a "simplify the predicate" refactor.
        var method = typeof(InsiderTradingFilingProcessor).GetMethod(
            "ParseBool",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        var result = (bool)method.Invoke(null, ["true"])!;

        result.Should().BeTrue();
    }
}
