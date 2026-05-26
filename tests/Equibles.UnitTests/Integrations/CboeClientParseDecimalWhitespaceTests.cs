using System.Reflection;
using Equibles.Integrations.Cboe;

namespace Equibles.UnitTests.Integrations;

public class CboeClientParseDecimalWhitespaceTests
{
    // Sibling to CboeClientParseLongWhitespaceTests. ParseDecimal reads the
    // PutCallRatio column (and the equity/index/VIX ratio variants). CBOE
    // occasionally emits a blank cell for thinly-traded sessions; the
    // `value?.Trim()` + `IsNullOrEmpty` short-circuit collapses those to a
    // null result instead of letting decimal.TryParse(" ") return its
    // coincidental false. A refactor that dropped the Trim arm (under the
    // assumption that decimal.TryParse on whitespace is null-equivalent)
    // would silently change the meaning of " " from "no data" to "decimal
    // parse failed" — both currently null today, but the former is the
    // intended contract and the latter is implementation accident.
    [Fact]
    public void ParseDecimal_WhitespaceOnlyInput_ReturnsNullWithoutThrowing()
    {
        var method = typeof(CboeClient).GetMethod(
            "ParseDecimal",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        decimal? parsed = 1m;
        var act = () => parsed = (decimal?)method.Invoke(null, ["   "]);

        act.Should().NotThrow();
        parsed.Should().BeNull();
    }
}
