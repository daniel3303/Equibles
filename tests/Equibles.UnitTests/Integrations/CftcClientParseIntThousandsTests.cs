using System.Reflection;
using Equibles.Integrations.Cftc;

namespace Equibles.UnitTests.Integrations;

public class CftcClientParseIntThousandsTests
{
    // Sibling to the ParseLong thousands-separator pin. CftcClient.ParseInt
    // reads CFTC trader-count columns (TradersTotal / TradersNonCommLong /
    // etc.) which can carry thousands-separator commas for large-volume
    // markets. The body strips commas via `.Replace(",", "")` before calling
    // int.TryParse with InvariantCulture — without the Replace, int.TryParse
    // under InvariantCulture's default NumberStyles.Integer rejects the
    // comma and returns null. A refactor that aligned ParseInt with the
    // comma-less ParseDecimal sibling (a tempting "consistency" cleanup)
    // would silently drop every comma-formatted trader-count column and
    // leave nulls scattered across CftcReportRecord rows.
    [Fact]
    public void ParseInt_ValueWithThousandSeparatorCommas_StripsAndParses()
    {
        var method = typeof(CftcClient).GetMethod(
            "ParseInt",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (int?)method.Invoke(null, ["1,234"]);

        result.Should().Be(1234);
    }
}
