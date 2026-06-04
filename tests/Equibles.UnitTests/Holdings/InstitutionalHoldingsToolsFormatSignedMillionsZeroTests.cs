using System.Reflection;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsFormatSignedMillionsZeroTests
{
    // FormatSignedMillions uses the three-part format "+#,##0.0;-#,##0.0;0.0".
    // The third (zero) section is distinct: an exactly-$0 delta must render "0.0"
    // with NO sign. The existing pin only covers the positive arm ("+1,234.5"),
    // so a regression to a two-part format ("+#,##0.0;-#,##0.0") — which applies
    // the positive section to zero — would mislabel a flat delta as "+0.0".
    [Fact]
    public void FormatSignedMillions_ZeroDelta_RendersUnsignedZeroNotPlusZero()
    {
        var method = typeof(InstitutionalHoldingsTools).GetMethod(
            "FormatSignedMillions",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, [0m]);

        result.Should().Be("0.0");
    }
}
