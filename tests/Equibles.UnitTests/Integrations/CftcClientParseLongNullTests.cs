using System.Reflection;
using Equibles.Integrations.Cftc;

namespace Equibles.UnitTests.Integrations;

public class CftcClientParseLongNullTests
{
    // Closes the per-helper null-guard family for CftcClient's three private
    // parsers:
    //   • ParseDecimal null guard — pinned by CftcClientParseDecimalNullTests
    //   • ParseInt null guard     — pinned by CftcClientParseIntNullTests (#2310)
    //   • ParseLong null guard    — this pin
    //
    // ParseLong reads the open-interest and position-count fields from the
    // CFTC CSV (OpenInterest, NonCommLong, NonCommShort, NonCommSpreads,
    // CommLong, CommShort, TotalRptLong, TotalRptShort, NonRptLong,
    // NonRptShort, ChangeOpenInterest, ChangeNonCommLong, … — twelve
    // fields on every weekly COT row).
    //
    // The contract on the null arm:
    //   if (value == null) return null;
    // CFTC's CSV omits cells for non-publishable values (statistical
    // suppression — e.g. when a single trader's reportable position
    // would otherwise be disclosed). The upstream `Get(...)` returns
    // null on those cells. ParseLong must absorb null and return null
    // so the long? column in CftcPositionReport stays nullable in the
    // database.
    //
    // The risk this pin uniquely catches and the existing siblings cannot:
    //   • Drop-the-null-guard in ParseLong — `value.Replace(",", "")` NREs
    //     on null. Each crash propagates up through ParseLine and aborts
    //     the entire weekly CFTC ingest. The ParseDecimal and ParseInt
    //     siblings defend THEIR helpers, not this one.
    //   • Swap-to-zero (`return 0;`) — would compile, pass the thousands-
    //     separator pin (its input is non-null), and silently replace
    //     every suppressed position-count cell with 0 in the database.
    //     Downstream open-interest aggregates would treat statistically-
    //     suppressed rows as "zero open interest" — visibly inflating
    //     the appearance of market thinness on the CFTC dashboard.
    //
    // The triad (ParseDecimal + ParseInt + ParseLong null guards) now
    // defends every nullable-numeric helper in CftcClient against the
    // same drop-the-guard refactor class.
    //
    // Pin: invoke with null; assert no throw and result is null.
    // Reflection-invoke since private static.
    [Fact]
    public void ParseLong_NullInput_ReturnsNullWithoutThrowing()
    {
        var method = typeof(CftcClient).GetMethod(
            "ParseLong",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        long? parsed = 1L;
        var act = () => parsed = (long?)method!.Invoke(null, new object[] { null });

        act.Should().NotThrow();
        parsed.Should().BeNull();
    }
}
