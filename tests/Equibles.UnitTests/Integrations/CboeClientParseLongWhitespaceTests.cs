using System.Reflection;
using Equibles.Integrations.Cboe;

namespace Equibles.UnitTests.Integrations;

public class CboeClientParseLongWhitespaceTests
{
    // CboeClient.ParseLong reads CSV cells for CallVolume / PutVolume /
    // TotalVolume from the daily put/call ratio file. CBOE occasionally
    // emits blank cells (whitespace-only) for thinly traded sessions —
    // the body's leading `value?.Trim()` then `IsNullOrEmpty` collapses
    // those to a null result rather than passing whitespace into
    // long.TryParse. A refactor that "simplified" the early-bail to a
    // bare null check (dropping the Trim or the IsNullOrEmpty branch)
    // would route " " into `value.Replace(",", "")` and either NRE or
    // succeed at TryParse(" ") = false → still null, but only by
    // coincidence — and a future refactor that pre-allocated a buffer
    // on the trimmed string would IOOR. Pin the whitespace short-circuit.
    [Fact]
    public void ParseLong_WhitespaceOnlyInput_ReturnsNullWithoutThrowing()
    {
        var method = typeof(CboeClient).GetMethod(
            "ParseLong",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        long? parsed = 1L;
        var act = () => parsed = (long?)method.Invoke(null, ["   "]);

        act.Should().NotThrow();
        parsed.Should().BeNull();
    }
}
