using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FormDFilingProcessorParseAmountWhitespaceTests
{
    [Fact]
    public void ParseAmount_WhitespacePaddedIndefinite_ReturnsNullFlagged()
    {
        // Contract: the literal "Indefinite" maps to (null, true). Form D XML element text
        // routinely carries surrounding newlines/indentation, so the match trims first. Existing
        // tests only pass unpadded "Indefinite"/"indefinite" — a regression dropping the Trim()
        // would still pass them while silently misreading real padded filings as a $0 amount.
        var result = FormDFilingProcessor.ParseAmount("  \n  Indefinite  \n");

        result.Should().Be(((long?)null, true));
    }
}
