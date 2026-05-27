using System.Reflection;
using Equibles.Integrations.Cftc;

namespace Equibles.UnitTests.Integrations;

public class CftcClientParseIntNullTests
{
    // Sibling to CftcClientParseDecimalNullTests. That pin defends
    // ParseDecimal's `value == null` guard for percentage-of-open-interest
    // fields. This pin covers the structurally identical guard in the
    // ParseInt helper, which reads trader-count fields from the CFTC CSV
    // (Traders_Tot_All, Traders_NonComm_Long_All, Traders_NonComm_Short_All,
    // Traders_Comm_Long_All, Traders_Comm_Short_All).
    //
    // CFTC's CSV occasionally omits trader-count cells for thinly traded
    // markets — the upstream `Get(...)` returns null when the column is
    // absent for that row. ParseInt must absorb null and return null,
    // not throw, so the downstream `record.TradersTotal = ParseInt(...)`
    // assignment propagates the missing-value semantics intact.
    //
    // The risk this pin uniquely catches and that the ParseDecimal sibling
    // cannot:
    //   • Drop-the-null-guard in ParseInt — `int.TryParse(value.Replace(",", ""),
    //     ...)` — would NRE at the `value.Replace` call on null input. The
    //     ParseDecimal sibling defends its own helper but doesn't reach this
    //     code path.
    //   • Swap-to-zero — `return 0;` instead of `return null;` — would compile,
    //     pass the thousands-separator pin (its input is non-null and parses
    //     successfully), and silently replace every missing trader-count
    //     field with 0 in the database. Downstream aggregates (rolling
    //     trader-count averages, market-depth indicators) would treat
    //     absent values as "zero traders" — visibly distorting the CFTC
    //     positioning dashboard for thin markets.
    //
    // Pin: invoke with null and assert the result is null and the call
    // does not throw. Reflection-invoke since ParseInt is private static.
    [Fact]
    public void ParseInt_NullInput_ReturnsNullWithoutThrowing()
    {
        var method = typeof(CftcClient).GetMethod(
            "ParseInt",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        int? parsed = 1;
        var act = () => parsed = (int?)method!.Invoke(null, new object[] { null });

        act.Should().NotThrow();
        parsed.Should().BeNull();
    }
}
