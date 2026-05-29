using System.Reflection;
using System.Text.Json;
using Equibles.Integrations.Cboe;

namespace Equibles.UnitTests.Integrations;

public class CboeClientParseRatiosTests
{
    // ParseRatios maps each well-formed {name,value} ratio entry to name->decimal,
    // and must skip malformed entries (missing value, empty name) rather than
    // throw or emit a junk key — robustness the cassette/happy-path tests don't
    // exercise. Pins that exactly the one valid entry survives a mixed array.
    [Fact]
    public void ParseRatios_MixedArray_KeepsWellFormedEntryAndSkipsMalformed()
    {
        var method = typeof(CboeClient).GetMethod(
            "ParseRatios",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        using var doc = JsonDocument.Parse(
            """
            {
              "ratios": [
                { "name": "TOTAL PUT/CALL RATIO", "value": "0.88" },
                { "name": "MISSING VALUE" },
                { "name": "", "value": "1.23" }
              ]
            }
            """
        );

        var result = (Dictionary<string, decimal?>)method!.Invoke(null, [doc.RootElement]);

        result.Should().ContainSingle();
        result["TOTAL PUT/CALL RATIO"].Should().Be(0.88m);
    }
}
