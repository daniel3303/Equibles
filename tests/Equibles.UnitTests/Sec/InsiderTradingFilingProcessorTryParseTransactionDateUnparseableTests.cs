using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorTryParseTransactionDateUnparseableTests
{
    // Sibling to TryParseTransactionDateHijriCultureTests (which pins the
    // happy-path InvariantCulture hardening). The failure-path arm —
    // unparseable input returning false WITHOUT throwing — is unpinned.
    //
    // The caller treats the false return as "drop the transaction": at
    // ParseTransaction.cs the guard reads `if (!TryParseTransactionDate(...))
    // return null;`. The whole point of the `TryParse` shape is to let one
    // malformed filing drop a single transaction instead of aborting the
    // batch and skipping every remaining filing in the worker cycle.
    //
    // The risks this pin uniquely catches and the Hijri sibling cannot:
    //   • Swap to `DateOnly.Parse` — would throw on malformed input
    //     instead of returning false. The Hijri sibling supplies a
    //     well-formed ISO date that .Parse would accept, so the
    //     regression slips past. The unparseable input here throws
    //     under .Parse and passes under .TryParse.
    //   • A "tighten" refactor that adds `?? throw new ArgumentException(...)`
    //     on the false branch — same observable: throw instead of false.
    //
    // Pin: TryParseTransactionDate("garbage", out _) returns false
    // without throwing. The dual assertion (.NotThrow + .BeFalse)
    // defends against both regression classes.
    [Fact]
    public void TryParseTransactionDate_UnparseableInput_ReturnsFalseWithoutThrowing()
    {
        var method = typeof(InsiderTradingFilingProcessor).GetMethod(
            "TryParseTransactionDate",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var args = new object[] { "not-a-date", default(DateOnly) };
        bool parsed = false;
        var act = () => parsed = (bool)method.Invoke(null, args);

        act.Should().NotThrow();
        parsed.Should().BeFalse();
    }
}
