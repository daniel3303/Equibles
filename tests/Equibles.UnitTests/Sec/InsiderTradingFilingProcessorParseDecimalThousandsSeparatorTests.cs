using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorParseDecimalThousandsSeparatorTests
{
    // Sibling to ParseDecimalNullInputTests and ParseDecimalUnparseableTests
    // (the two existing failure-path pins). The happy-path with a
    // THOUSANDS SEPARATOR — "1,234.56" → 1234.56m — is unpinned.
    //
    // The contract uses `NumberStyles.Any` explicitly. SEC Form 4 filings
    // occasionally render large transaction prices with thousands
    // separators ("$1,234.56 per share") — typed-by-hand exhibit
    // amendments and legacy paper-filing transcriptions both produce
    // this shape. The `NumberStyles.Any` choice is what makes the parse
    // tolerant of the comma.
    //
    // The risks this pin uniquely catches and the two failure-path
    // siblings cannot:
    //   • Narrowing to `NumberStyles.Float` (a "tighten — only accept
    //     bare numerics" cleanup). Float disallows thousands
    //     separators, so "1,234.56" fails to parse and returns 0.
    //     Existing tests pass (null → 0, unparseable → 0); this pin
    //     fails because the expected value is 1234.56m.
    //   • Culture swap to comma-decimal locales (e.g. fr-FR / de-DE).
    //     The explicit `CultureInfo.InvariantCulture` argument prevents
    //     this; a regression that dropped it would treat "1,234.56" as
    //     "1.23456" (comma as decimal separator). Pinning the exact
    //     value 1234.56m surfaces both regressions.
    //
    // Pin: ParseDecimal("1,234.56") returns 1234.56m.
    [Fact]
    public void ParseDecimal_ValueWithThousandsSeparator_ParsesViaNumberStylesAny()
    {
        var method = typeof(InsiderTradingFilingProcessor).GetMethod(
            "ParseDecimal",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (decimal)method.Invoke(null, ["1,234.56"]);

        result.Should().Be(1234.56m);
    }
}
