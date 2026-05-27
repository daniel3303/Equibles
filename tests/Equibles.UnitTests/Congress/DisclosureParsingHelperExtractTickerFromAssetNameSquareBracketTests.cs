using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperExtractTickerFromAssetNameSquareBracketTests
{
    // Sibling to DisclosureParsingHelperExtractTickerClassShareTests
    // (the dotted class-share pin "BRK.B"). The TickerRegex is:
    //     [\(\[]\s*([A-Za-z]{1,5}(?:\.[A-Za-z]{1,2})?)\s*[\)\]]
    // which deliberately accepts BOTH parenthesis-wrapped AND
    // square-bracket-wrapped tickers — accommodating PDF/OCR-extracted
    // disclosures that emit "Apple Inc [AAPL]" instead of the
    // canonical "Apple Inc (AAPL)". The square-bracket form is real:
    // some Senate financial-disclosure PDF templates render the
    // ticker in brackets, and the PDF→HTML extractor preserves them
    // verbatim.
    //
    // The risks this pin uniquely catches and the existing dotted
    // sibling cannot:
    //
    //   • Bracket char-class drop — `[\(]\s*...\s*[\)]` (someone
    //     "tightens" the regex assuming only parentheses are
    //     legitimate) would compile, pass the existing dotted-share
    //     pin (uses parentheses), and silently drop every
    //     square-bracket-formatted ticker. Affected disclosures
    //     would lose their stock linkage entirely — those trades
    //     would render with "Unknown" ticker in the
    //     congressional-trades dashboard.
    //
    //   • ToUpperInvariant drop — the existing pin uses uppercase
    //     "BRK.B" in input, so a refactor that dropped the
    //     `.ToUpperInvariant()` call would still pass it. A
    //     lowercase ticker in the input EXERCISES the normalisation
    //     directly. Real disclosures sometimes have lowercase
    //     tickers from data-entry inconsistency ("apple inc
    //     (aapl)"); the downstream `WHERE Ticker = @ticker` query
    //     is case-sensitive in Postgres by default, so missing
    //     normalization means missed lookups.
    //
    // Adversarial input: a lowercase ticker inside SQUARE BRACKETS
    // — combines both unpinned axes. Result must be uppercase
    // "AAPL" via the regex match + ToUpperInvariant. Failure
    // mode distinguishes: a null result means the regex didn't
    // match (bracket drop), a lowercase "aapl" result means the
    // upper-case normalization was dropped.
    [Fact]
    public void ExtractTickerFromAssetName_LowercaseTickerInSquareBrackets_ExtractsAndUppercases()
    {
        var result = DisclosureParsingHelper.ExtractTickerFromAssetName("Apple Inc [aapl]");

        result.Should().Be("AAPL");
    }
}
