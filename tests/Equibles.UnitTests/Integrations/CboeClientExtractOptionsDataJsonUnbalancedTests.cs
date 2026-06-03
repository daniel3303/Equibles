using System.Reflection;
using Equibles.Integrations.Cboe;

namespace Equibles.UnitTests.Integrations;

public class CboeClientExtractOptionsDataJsonUnbalancedTests
{
    // ExtractOptionsDataJson returns the balanced `{...}` after the optionsData marker.
    // Contract: when the object opens but its braces never balance — a CBOE page truncated
    // mid-payload — there is no complete object to return, so it yields null rather than a
    // garbled partial substring. Pins the depth-never-zero / end<0 guard.
    [Fact]
    public void ExtractOptionsDataJson_UnbalancedObjectNeverCloses_ReturnsNull()
    {
        var method = typeof(CboeClient).GetMethod(
            "ExtractOptionsDataJson",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // Marker + an object that opens '{' and ends mid-field with no closing '}'.
        var html = "<script>x = {\"optionsData\\\":" + "{\\\"label\\\":\\\"a\\\"";

        var result = (string)method!.Invoke(null, [html]);

        result.Should().BeNull();
    }
}
