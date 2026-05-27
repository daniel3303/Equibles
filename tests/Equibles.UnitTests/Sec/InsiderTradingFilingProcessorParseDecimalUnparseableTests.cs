using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorParseDecimalUnparseableTests
{
    // Third pin in the ParseDecimal family:
    //   • Thousands-separator success (existing — InsiderTradingFilingProcessorTests)
    //   • Null-input guard returns 0 (PR #2302)
    //   • Unparseable input returns 0 (this pin — the TryParse FAIL arm)
    //
    // The structurally distinct arm here is the FALSE branch of the
    // ternary at the bottom:
    //   decimal.TryParse(value, NumberStyles.Any, InvariantCulture, out var r)
    //       ? r
    //       : 0;
    //                            ^ this branch
    //
    // The contract: on a non-null, non-empty, but unparseable string
    // (e.g. SEC publishes "N/A" or "-" in legacy Form 4 quantitative
    // fields, or a future schema introduces a placeholder like "TBD"),
    // ParseDecimal returns 0 instead of throwing. Downstream callers
    // treat 0 as "field absent" — the same as the null-guard return —
    // so the insider-transaction row imports with a zero quantity
    // rather than aborting the batch.
    //
    // The risk this pin uniquely catches:
    //   • Drop-the-fallback-zero — `: throw new FormatException(...)`
    //     under "tighten up the silent-on-failure" pass — would
    //     compile, pass the existing thousands-separator pin (success
    //     arm, not the fail arm), pass the null-input guard pin
    //     (different path), and crash every Form 4 filing that
    //     carries "N/A" / "-" / "TBD" in a price or share-count
    //     field. Legacy filings and amendments routinely contain
    //     these placeholders in fields the filer wasn't able to
    //     compute at filing time. Each crash aborts the batch and
    //     skips every remaining filing in the worker cycle.
    //   • Swap-to-sentinel — `: decimal.MaxValue` or `: -1m` — would
    //     compile, propagate sentinel values to the database, and
    //     poison every aggregate query (totals, averages, top-N
    //     ranks). The null-guard sibling can't see this because its
    //     input short-circuits before the TryParse call; the
    //     thousands-separator sibling can't see this because its
    //     input succeeds via the success arm.
    //
    // Pin: invoke with "N/A" (the SEC's most common placeholder in
    // pre-2010 Form 4 filings) and assert exactly 0m. Reflection-
    // invoke since internal static. The pair with the null-guard
    // sibling covers BOTH ways the helper returns 0 (guard short-
    // circuit vs TryParse fail-through); together they defend
    // every silent-zero path that downstream callers rely on.
    [Fact]
    public void ParseDecimal_UnparseableNonNumericInput_ReturnsZeroWithoutThrowing()
    {
        var method = typeof(InsiderTradingFilingProcessor).GetMethod(
            "ParseDecimal",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var act = () => (decimal)method!.Invoke(null, ["N/A"]);

        var result = act.Should().NotThrow().Subject;
        result.Should().Be(0m);
    }
}
