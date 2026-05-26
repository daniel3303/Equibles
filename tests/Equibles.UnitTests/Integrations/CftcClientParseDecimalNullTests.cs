using System.Reflection;
using Equibles.Integrations.Cftc;

namespace Equibles.UnitTests.Integrations;

public class CftcClientParseDecimalNullTests
{
    // CftcClient.ParseDecimal reads percentage-of-open-interest fields from the
    // CFTC CSV (Pct_of_OI_NonComm_Long_All and siblings) — the same record
    // builder that fills TradersTotal and a dozen other fields. CFTC's CSV
    // occasionally omits cells entirely for thinly traded markets, so the
    // upstream `Get("Pct_…")` returns null and ParseDecimal must absorb it.
    // The explicit `value == null` guard is the load-bearing safety — a
    // refactor that "simplified" it to `decimal.TryParse(value, …)` (dropping
    // the null arm) would NRE the moment the CFTC CSV omitted any percentage
    // column, aborting the entire weekly COT batch import.
    [Fact]
    public void ParseDecimal_NullInput_ReturnsNullWithoutThrowing()
    {
        var method = typeof(CftcClient).GetMethod(
            "ParseDecimal",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        decimal? parsed = 1m;
        var act = () => parsed = (decimal?)method.Invoke(null, [null]);

        act.Should().NotThrow();
        parsed.Should().BeNull();
    }
}
