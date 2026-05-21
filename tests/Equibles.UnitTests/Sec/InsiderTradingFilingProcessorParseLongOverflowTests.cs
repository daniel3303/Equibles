using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorParseLongOverflowTests
{
    private static readonly MethodInfo ParseLongMethod =
        typeof(InsiderTradingFilingProcessor).GetMethod(
            "ParseLong",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // Every sibling Parse* helper in InsiderTradingFilingProcessor (ParseBool,
    // ParseDecimal, ParseTransactionCode, TryParseTransactionDate) returns a
    // safe default on bad input rather than throwing — the SEC Form 4 XML feed
    // is user-submitted and routinely dirty, and the caller chain (ParseAllTransactions
    // → Process) treats a thrown exception as "drop the whole filing", so a
    // single bad row would wipe an entire insider's quarterly disclosure.
    //
    // ParseLong's decimal fallback breaks that convention for one specific
    // input shape: a digit-only string that overflows `long` but parses as a
    // decimal. `long.TryParse` fails (out of range), execution falls to
    // `(long)ParseDecimal(value)`, decimal.TryParse succeeds (decimal holds
    // up to ~7.9e28), and the explicit `(long)decimal` cast then throws
    // OverflowException because the decimal exceeds long.MaxValue (~9.2e18).
    //
    // Contract under test: ParseLong must not throw on any string input,
    // matching the defensive pattern of its sibling Parse* helpers. The
    // existing decimal-fallback pin covers the well-formed-fractional case
    // ("1234.5678"); this pin covers the overflow case that would crash the
    // filing processor.
    [Fact(Skip = "GH-1673 — ParseLong throws OverflowException on out-of-range digit-only input")]
    public void ParseLong_OverflowDecimalString_DoesNotThrow()
    {
        // 19 nines = 9_999_999_999_999_999_999 ≈ 1.0e19, strictly greater than
        // long.MaxValue (~9.22e18) but well inside decimal's range. Picks the
        // exact branch: long.TryParse rejects (out of range), decimal.TryParse
        // accepts, and the cast overflows.
        var act = () => ParseLongMethod.Invoke(null, ["9999999999999999999"]);

        // TargetInvocationException unwrapping: reflection rewraps the inner
        // exception, so the assertion targets the wrapper class. NotThrow
        // catches both the wrapped OverflowException and any other crash path.
        act.Should().NotThrow();
    }
}
